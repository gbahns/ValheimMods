using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static GrabMaterialsMod.GrabMaterialsMod;

namespace GrabMaterials
{
	// Vanilla has exactly one gate on a container's contents: the open request.  Container.Interact
	// refuses a warded chest and one you may not open, and the ZDO owner's RPC_RequestOpen refuses
	// one that is in use, then hands ownership to the requester so their changes save.  Nothing in
	// the game stops code from reaching into the Inventory directly, which is what this mod does,
	// so it has to repeat both halves of that gate itself.  Without the first half a Personal rack
	// or a chest inside someone else's ward was grabbed from like any other.  Without the second
	// half the grab added the item to the player's inventory (owned, so saved) and removed it from
	// the local copy of a container another machine owned (not saved), so the next sync put the
	// item back: a duplicate on a grab, and a loss on a store.
	//
	// Container.CheckAccess is private at runtime, so its rule is repeated here rather than called.
	internal static class ContainerAccess
	{
		// The ZNetView a container saves through.  Carts keep it on the root object.
		internal static ZNetView NetView(Container container)
		{
			if (container == null) return null;
			return container.m_rootObjectOverride != null ? container.m_rootObjectOverride : container.GetComponent<ZNetView>();
		}

		// True when vanilla would open this container for the local player right now.
		internal static bool CanOpen(Container container, out string reason)
		{
			reason = null;
			if (container == null) return false;

			if (container.m_checkGuardStone && !PrivateArea.CheckAccess(container.transform.position, 0f, false, false))
			{
				reason = "warded";
				return false;
			}

			switch (container.m_privacy)
			{
				case Container.PrivacySetting.Public:
					break;
				case Container.PrivacySetting.Private:
					var piece = container.GetComponent<Piece>();
					var playerID = Game.instance.GetPlayerProfile().GetPlayerID();
					if (piece == null || piece.GetCreator() != playerID)
					{
						reason = "personal";
						return false;
					}
					break;
				default:
					reason = "group";
					return false;
			}

			// Container.IsInUse() is a local field only the owner keeps current; the owner publishes
			// it on the ZDO for everyone else, which is what draws the open-lid visual.
			var nview = NetView(container);
			if (nview != null && nview.IsValid())
			{
				var inUse = nview.IsOwner() ? container.IsInUse() : nview.GetZDO().GetInt(ZDOVars.s_inUse) == 1;
				if (inUse)
				{
					reason = "in use";
					return false;
				}
			}
			return true;
		}

		// Last check before a write: this machine must own the container, or Container.Save
		// will not run and the change is lost on the next sync.  Ownership is normally obtained
		// through Acquire below.  A container nobody owns (the two seconds after a server restart,
		// before the server assigns it to someone) is claimed outright, since there is no other
		// writer to conflict with.  One that another machine owns is never taken unilaterally:
		// for one round trip both machines would believe they own it, the server keeps whichever
		// write lands first and drops the other, and the losing player's own inventory, which
		// always saves, keeps the item.  That is a duplicate two cooperating players could farm.
		internal static bool EnsureOwned(Container container)
		{
			var nview = NetView(container);
			if (nview == null || !nview.IsValid()) return true; // nothing networked to own
			if (!nview.IsOwner() && !nview.HasOwner()) nview.ClaimOwnership();
			return nview.IsOwner();
		}

		// ── The open handshake ──────────────────────────────────────────────────────────────
		//
		// Vanilla never opens a chest directly.  Container.Interact sends RPC_RequestOpen to the
		// ZDO's owner; the owner checks in-use and access on its own, current state, pushes its
		// copy of the contents to the requester, hands ownership over, and only then replies
		// RPC_OpenResponse.  One writer at a time, with the checks made where they are current.
		// A grab goes through exactly that request, so it is granted or refused by the same
		// machine under the same rules as pressing Use, against any build of the game.  The one
		// difference is on this side: vanilla's reply handler opens the chest UI, and the prefix
		// below swallows the reply for a container a grab is waiting on.
		//
		// Containers this machine already owns, which is nearly all of them when playing alone,
		// skip the exchange and the whole thing completes synchronously.

		private const float GrantTimeout = 0.75f;

		// Containers with a request in flight, and the owner's answer once it arrives.
		internal static readonly Dictionary<Container, bool?> Requests = new Dictionary<Container, bool?>();

		// Resolve ownership of every container in the list, then hand the ones this machine owns
		// to onReady.  Synchronous when nothing has to be asked for.
		internal static void Acquire(List<Container> containers, Action<List<Container>> onReady)
		{
			var owned = new List<Container>();
			var pending = new List<Container>();
			var playerID = Game.instance.GetPlayerProfile().GetPlayerID();
			foreach (var container in containers)
			{
				if (container == null) continue;
				var nview = NetView(container);
				if (nview == null || !nview.IsValid() || nview.IsOwner()) { owned.Add(container); continue; }
				if (!nview.HasOwner())
				{
					nview.ClaimOwnership();
					if (nview.IsOwner()) owned.Add(container);
					continue;
				}
				if (Requests.ContainsKey(container)) continue; // a previous grab is still asking
				Requests[container] = null;
				nview.InvokeRPC("RPC_RequestOpen", playerID);
				pending.Add(container);
			}
			if (pending.Count == 0)
			{
				onReady(owned);
				return;
			}
			Log.LogInfo($"asking the owners of {pending.Count} container(s) to hand them over");
			Instance.StartCoroutine(AwaitGrants(owned, pending, onReady));
		}

		private static bool Settled(Container container)
		{
			if (container == null) return true;
			if (!Requests.TryGetValue(container, out var answer) || !answer.HasValue) return false;
			// A grant counts once the handed-over ZDO has arrived too; it is sent just ahead of the reply.
			return !answer.Value || NetView(container).IsOwner();
		}

		private static IEnumerator AwaitGrants(List<Container> owned, List<Container> pending, Action<List<Container>> onReady)
		{
			var deadline = Time.unscaledTime + GrantTimeout;
			while (Time.unscaledTime < deadline)
			{
				var all = true;
				foreach (var container in pending)
					if (!Settled(container)) { all = false; break; }
				if (all) break;
				yield return null;
			}
			foreach (var container in pending)
			{
				Requests.TryGetValue(container, out var answer);
				Requests.Remove(container);
				if (container == null) continue;
				if (answer == true && NetView(container).IsOwner())
				{
					owned.Add(container);
					continue;
				}
				var why = answer == null ? "its owner did not answer" : answer == false ? "its owner refused (in use or not yours)" : "it was granted but ownership never arrived";
				Log.LogInfo($"skipping {container.name} {container.GetInstanceID()}: {why}");
			}
			onReady(owned);
		}
	}

	// Vanilla's reply handler opens the chest UI on a grant and shows "$msg_inuse" on a refusal.
	// For a container a grab asked about, record the answer instead and do neither.
	[HarmonyPatch(typeof(Container), "RPC_OpenResponse")]
	internal static class ContainerOpenResponsePatch
	{
		private static bool Prefix(Container __instance, bool granted)
		{
			if (!ContainerAccess.Requests.ContainsKey(__instance)) return true;
			ContainerAccess.Requests[__instance] = granted;
			return false;
		}
	}
}

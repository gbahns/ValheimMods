using UnityEngine;

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

		// Make this machine the container's owner before changing its inventory, the way the
		// vanilla open handshake does, so Container.Save actually runs.  Claiming is what vanilla
		// itself does on interaction with doors, ships and the like.  Returns false when the
		// container cannot be owned, in which case nothing should be written to it.
		internal static bool EnsureOwned(Container container)
		{
			var nview = NetView(container);
			if (nview == null || !nview.IsValid()) return true; // nothing networked to own
			if (!nview.IsOwner()) nview.ClaimOwnership();
			return nview.IsOwner();
		}
	}
}

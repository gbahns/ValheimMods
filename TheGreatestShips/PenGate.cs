using UnityEngine;

namespace TheGreatestShips
{
    /// <summary>
    /// The pen's gate: a section of the starboard rails hinged along its base at the rail line.
    /// Open, it swings outboard and down to lie about 25 degrees above the deck -- a ramp from the
    /// pen floor up over the gunwale, which at midships is only half a meter above the deck and
    /// a meter outboard of the rails.  The rails and the flat wall behind them rotate with it, so
    /// the ramp is a solid surface; the lip above stays put (it is only on under way anyway).
    ///
    /// The vanilla gate can't be used: its Door script needs a ZNetView of its own, and a
    /// ZNetView only works on a prefab's root.  So the open/closed state lives in the ship's ZDO,
    /// which every client already has and which survives reload; a press by anyone but the
    /// ship's owner goes to the owner by RPC, as vanilla doors do.
    /// </summary>
    internal sealed class PenGate : MonoBehaviour, Hoverable, Interactable
    {
        private const float SwingAngle = 65f;   // about the hinge (local Z): the top swings outboard and down, to ~25 degrees above the deck
        private const float SwingSpeed = 120f;  // degrees per second

        // Set when built.  Public so Unity serializes it: Instantiate copies serialized fields
        // only, and an internal one left every spawned ship with two starboard gates.
        public bool Port;   // the port gate keeps its own state and swings the other way

        private string ZdoKey    => Port ? "DM_PenGateOpenPort" : "DM_PenGateOpen";
        private string RpcName   => Port ? "DM_PenGatePort" : "DM_PenGate";
        private float  OpenAngle => Port ? SwingAngle : -SwingAngle;   // +X is starboard; a positive Z rotation tips +Y toward -X

        private ZNetView _nview;
        private bool     _open;
        private float    _next;

        private void Start()
        {
            _nview = GetComponentInParent<ZNetView>();
            if (_nview == null || !_nview.IsValid()) { _nview = null; return; }
            try { _nview.Register<bool>(RpcName, RPC_SetOpen); }
            catch (System.ArgumentException) { /* already registered on this ship: same gate, same handler */ }
            _open = _nview.GetZDO().GetBool(ZdoKey);
            transform.localRotation = Target();
        }

        private void RPC_SetOpen(long sender, bool open)
        {
            if (_nview != null && _nview.IsOwner())
                _nview.GetZDO().Set(ZdoKey, open);
        }

        private void Update()
        {
            if (_nview == null) return;
            if (Time.time >= _next)
            {
                _next = Time.time + 0.25f;
                _open = _nview.GetZDO().GetBool(ZdoKey);
            }
            var target = Target();
            if (transform.localRotation != target)
                transform.localRotation = Quaternion.RotateTowards(transform.localRotation, target, SwingSpeed * Time.deltaTime);
        }

        private Quaternion Target() => Quaternion.Euler(0f, 0f, _open ? OpenAngle : 0f);

        public string GetHoverText() =>
            Localization.instance.Localize($"Pen gate\n[<color=yellow><b>$KEY_Use</b></color>] {(_open ? "Close" : "Open")}");

        public string GetHoverName() => "Pen gate";

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || _nview == null) return false;
            bool open = !_nview.GetZDO().GetBool(ZdoKey);
            if (_nview.IsOwner()) _nview.GetZDO().Set(ZdoKey, open);
            else _nview.InvokeRPC(RpcName, open);
            _next = 0f; // pick the change up on the next frame
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }

    /// <summary>
    /// Player.FindHoverObject only takes the looked-at object as the hover target when the
    /// Hoverable sits on the collider's own GameObject; otherwise the hover goes to the
    /// rigidbody's object, the ship, which has no hover text.  So every collider under the gate
    /// carries one of these, forwarding to the PenGate above it.
    /// </summary>
    internal sealed class PenGateHandle : MonoBehaviour, Hoverable, Interactable
    {
        private PenGate _gate;
        private PenGate Gate => _gate != null ? _gate : (_gate = GetComponentInParent<PenGate>());

        public string GetHoverText() => Gate != null ? Gate.GetHoverText() : "";
        public string GetHoverName() => Gate != null ? Gate.GetHoverName() : "";
        public float  GetHoverOffset() => 0f;
        public bool Interact(Humanoid user, bool hold, bool alt) => Gate != null && Gate.Interact(user, hold, alt);
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}

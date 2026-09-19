using UnityEngine;

namespace WeaponControl.Core
{
    /// <summary>
    /// Central access point for the generated Input System wrapper (InputSystem_Actions).
    /// All gameplay systems (camera, movement, weapons) read input through here so we
    /// share a single enabled instance instead of each creating their own.
    /// </summary>
    public static class GameInput
    {
        private static InputSystem_Actions _actions;

        /// <summary>Lazily-created, always-enabled input actions instance.</summary>
        public static InputSystem_Actions Actions
        {
            get
            {
                if (_actions == null)
                {
                    _actions = new InputSystem_Actions();
                    _actions.Enable();
                }
                return _actions;
            }
        }

        /// <summary>Shortcut to the Player action map.</summary>
        public static InputSystem_Actions.PlayerActions Player => Actions.Player;

        // With "Enter Play Mode Options" (domain reload disabled) the static field would
        // survive between play sessions and point at a disposed instance. Reset it on load.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _actions = null;
        }
    }
}

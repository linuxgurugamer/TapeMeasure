using UnityEngine;
using ToolbarControl_NS;

namespace TapeMeasure
{
    /// <summary>
    /// Registers TapeMeasure with ToolbarController early so the user's
    /// stock/Blizzy toolbar preference is managed by ToolbarController itself.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class RegisterToolbar : MonoBehaviour
    {
        private void Start()
        {
            ToolbarControl.RegisterMod(TapeMeasure.ModId, TapeMeasure.ModName);
        }
    }
}

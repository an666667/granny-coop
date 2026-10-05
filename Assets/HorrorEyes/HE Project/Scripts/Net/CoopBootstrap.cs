using UnityEngine;

namespace GrannyCoop
{
    /// <summary>
    /// Creates the co-op manager + HUD automatically at startup, so the game
    /// scenes never have to be edited by hand.
    /// </summary>
    public static class CoopBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (CoopSession.Instance == null)
            {
                var go = new GameObject("CoopSession");
                go.AddComponent<CoopSession>();
                NetConfig.Log("coop session created");
            }

            if (Object.FindObjectOfType<CoopHud>() == null)
            {
                var hud = new GameObject("CoopHud");
                hud.AddComponent<CoopHud>();
            }
        }
    }
}

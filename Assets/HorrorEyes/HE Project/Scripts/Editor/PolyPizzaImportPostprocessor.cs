using UnityEditor;
using UnityEngine;

namespace GrannyCoop.EditorTools
{
    /// <summary>
    /// Ensures the Poly.pizza character models are imported with Legacy animation,
    /// so the runtime avatar can play their Walk/Run/Idle clips without any manual setup.
    /// </summary>
    public class PolyPizzaImportPostprocessor : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (assetPath == null || !assetPath.Contains("PolyPizza")) return;

            var mi = assetImporter as ModelImporter;
            if (mi == null) return;

            mi.animationType = ModelImporterAnimationType.Legacy;
            mi.importAnimation = true;
            mi.animationCompression = ModelImporterAnimationCompression.Off;
            Debug.Log("[Coop] Poly.pizza model set to Legacy animation: " + assetPath);
        }
    }
}

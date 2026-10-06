using System.Collections.Generic;
using UnityEngine;

namespace GrannyCoop
{
    public class CoopFurniture : MonoBehaviour
    {
        public string Id = "bed_01";
        public bool IsBed = true;
        public const float BedClearance = 0.6f;
        public static readonly List<CoopFurniture> All = new List<CoopFurniture>();
        readonly List<string> _occupants = new List<string>();
        public bool IsOccupied { get { return _occupants.Count > 0; } }

        public void SetOccupant(string playerId, bool inside)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            bool has = _occupants.Contains(playerId);
            if (inside && !has) _occupants.Add(playerId);
            else if (!inside && has) _occupants.Remove(playerId);
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        static readonly Vector3[] BedOffsets =
        {
            new Vector3( 2.6f, 0f,  3.2f),
            new Vector3(-3.1f, 0f,  4.4f),
        };

        public static void SpawnForScene(Transform anchor)
        {
            foreach (var f in All.ToArray()) if (f != null) Destroy(f.gameObject);
            All.Clear();
            if (anchor == null) { NetConfig.LogError("furniture: no anchor"); return; }

            for (int i = 0; i < BedOffsets.Length; i++)
            {
                Vector3 want = anchor.position + anchor.rotation * BedOffsets[i];
                RaycastHit hit;
                if (Physics.Raycast(want + Vector3.up * 2f, Vector3.down, out hit, 8f))
                    want = hit.point;

                var go = BuildBed(i == 0 ? "bed_01" : "bed_02");
                go.transform.position = want;
                go.transform.rotation = Quaternion.Euler(0f, anchor.eulerAngles.y, 0f);
                var f2 = go.GetComponent<CoopFurniture>();
                NetConfig.Log("furniture: " + f2.Id + " at " + want.ToString("F2"));
            }
        }

        static GameObject BuildBed(string id)
        {
            var root = new GameObject("Coop_" + id);
            var f = root.AddComponent<CoopFurniture>();
            f.Id = id;
            f.IsBed = true;

            const float w = 2.00f, l = 1.10f;
            const float legH = BedClearance;
            const float frameT = 0.18f;
            const float matT = 0.25f;

            var wood = MakeMat(new Color(0.36f, 0.24f, 0.16f));
            var cloth = MakeMat(new Color(0.72f, 0.70f, 0.66f));
            var pillow = MakeMat(new Color(0.90f, 0.90f, 0.92f));

            float lx = w * 0.5f - 0.08f, lz = l * 0.5f - 0.08f;
            float[] xs = { -lx, lx };
            float[] zs = { -lz, lz };
            foreach (float x in xs)
                foreach (float z in zs)
                    AddBox(root.transform, "Leg", new Vector3(x, legH * 0.5f, z),
                        new Vector3(0.14f, legH, 0.14f), wood, true);

            AddBox(root.transform, "Frame", new Vector3(0f, legH + frameT * 0.5f, 0f),
                new Vector3(w, frameT, l), wood, false);
            AddBox(root.transform, "Mattress", new Vector3(0f, legH + frameT + matT * 0.5f, 0f),
                new Vector3(w * 0.96f, matT, l * 0.96f), cloth, false);
            AddBox(root.transform, "Pillow", new Vector3(-w * 0.5f + 0.42f, legH + frameT + matT + 0.06f, 0f),
                new Vector3(0.62f, 0.12f, l * 0.8f), pillow, false);

            return root;
        }

        static void AddBox(Transform parent, string name, Vector3 localPos, Vector3 size,
                           Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
            if (!collider)
            {
                var c = go.GetComponent<Collider>();
                if (c != null) Destroy(c);
            }
        }

        static Material MakeMat(Color c)
        {
            var sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Mobile/Diffuse");
            if (sh == null) return null;
            var m = new Material(sh);
            m.color = c;
            return m;
        }
    }
}

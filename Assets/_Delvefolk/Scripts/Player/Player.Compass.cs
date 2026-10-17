using UnityEngine;

namespace Delvefolk
{
    // Brújula maldita: una flecha alrededor del jugador señala la salida (si está a más de 3; no en los bonus)
    public sealed partial class Player
    {
        Transform compassArrow;
        static Mesh arrowMesh;

        partial void UpdateCompass(float dt)
        {
            var d = G.Dungeon;
            bool on = Gear.Has("item_compass") && d != null && d.HasExit && !G.InBonus && !Dead;
            if (!on) { if (compassArrow && compassArrow.gameObject.activeSelf) compassArrow.gameObject.SetActive(false); return; }
            if (!compassArrow)
            {
                if (!arrowMesh)
                {
                    // Cono de 4 lados tumbado hacia delante, a 1.05 del centro
                    arrowMesh = new MeshBuilder().Add(ProcMesh.Cone(0.16f, 0.42f, 4).RotateX(Mathf.PI / 2f), new Vector3(0, 0, 1.05f), new Color(0.4f, 0.867f, 1f, 0.85f)).ToMesh("flechaBrujula");
                }
                var go = new GameObject("Brujula");
                go.transform.SetParent(Root, false);
                go.transform.localPosition = new Vector3(0, 0.15f, 0);
                CharacterModel.MeshObject(go.transform, "Flecha", arrowMesh, Mats.Transparent, Vector3.zero);
                compassArrow = go.transform;
            }
            bool far = MathUtil.Hypot(d.ExitX - X, d.ExitZ - Z) > 3f;
            if (compassArrow.gameObject.activeSelf != far) compassArrow.gameObject.SetActive(far);
            // La raíz del jugador gira con su orientación: la flecha se orienta en coordenadas del mundo
            compassArrow.rotation = Quaternion.Euler(0, Mathf.Atan2(d.ExitX - X, d.ExitZ - Z) * Mathf.Rad2Deg, 0);
        }

        public bool CompassVisible { get { return compassArrow && compassArrow.gameObject.activeInHierarchy; } }
        public float CompassYaw { get { return compassArrow ? compassArrow.eulerAngles.y * Mathf.Deg2Rad : 0f; } }
    }
}

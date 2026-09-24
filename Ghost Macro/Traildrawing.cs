using UnityEngine;

namespace GhostMacro
{
    internal static class TrailDrawing
    {
        private static Material lineMaterial;
        private static bool isDrawing;

        private static void EnsureMaterial()
        {
            if (lineMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            lineMaterial.SetInt("_ZWrite", 0);
        }

        public static void Begin()
        {
            EnsureMaterial();
            lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.TRIANGLES);
            isDrawing = true;
        }

        public static void End()
        {
            if (!isDrawing)
            {
                return;
            }

            GL.End();
            GL.PopMatrix();
            isDrawing = false;
        }
        public static void DrawLine(Vector2 pointA, Vector2 pointB, Color color, float width)
        {
            float dx = pointB.x - pointA.x;
            float dy = pointB.y - pointA.y;
            float len = Mathf.Sqrt(dx * dx + dy * dy);

            if (len < 0.0001f)
            {
                return;
            }

            float halfWidth = width * 0.5f;
            float nx = -dy / len * halfWidth;
            float ny = dx / len * halfWidth;

            Vector2 a0 = new Vector2(pointA.x + nx, pointA.y + ny);
            Vector2 a1 = new Vector2(pointA.x - nx, pointA.y - ny);
            Vector2 b0 = new Vector2(pointB.x + nx, pointB.y + ny);
            Vector2 b1 = new Vector2(pointB.x - nx, pointB.y - ny);

            GL.Color(color);

            GL.Vertex3(a0.x, a0.y, 0);
            GL.Vertex3(b0.x, b0.y, 0);
            GL.Vertex3(b1.x, b1.y, 0);

            GL.Vertex3(a0.x, a0.y, 0);
            GL.Vertex3(b1.x, b1.y, 0);
            GL.Vertex3(a1.x, a1.y, 0);
        }
        public static void DrawSmoothLine(Vector2 pointA, Vector2 pointB, Color color, float width, float featherWidth = 0.02f)
        {
            float dx = pointB.x - pointA.x;
            float dy = pointB.y - pointA.y;
            float len = Mathf.Sqrt(dx * dx + dy * dy);

            if (len < 0.0001f)
            {
                return;
            }

            float halfWidth = width * 0.5f;
            Vector2 normal = new Vector2(-dy / len, dx / len);
            Color transparent = new Color(color.r, color.g, color.b, 0f);

            DrawQuad(
                pointA + normal * halfWidth, pointB + normal * halfWidth,
                pointB - normal * halfWidth, pointA - normal * halfWidth,
                color, color, color, color);

            DrawQuad(
                pointA + normal * halfWidth, pointB + normal * halfWidth,
                pointB + normal * (halfWidth + featherWidth), pointA + normal * (halfWidth + featherWidth),
                color, color, transparent, transparent);

            DrawQuad(
                pointA - normal * halfWidth, pointB - normal * halfWidth,
                pointB - normal * (halfWidth + featherWidth), pointA - normal * (halfWidth + featherWidth),
                color, color, transparent, transparent);
        }

        private static void DrawQuad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Color c0, Color c1, Color c2, Color c3)
        {
            GL.Color(c0); GL.Vertex3(p0.x, p0.y, 0);
            GL.Color(c1); GL.Vertex3(p1.x, p1.y, 0);
            GL.Color(c2); GL.Vertex3(p2.x, p2.y, 0);

            GL.Color(c0); GL.Vertex3(p0.x, p0.y, 0);
            GL.Color(c2); GL.Vertex3(p2.x, p2.y, 0);
            GL.Color(c3); GL.Vertex3(p3.x, p3.y, 0);
        }
    }
}
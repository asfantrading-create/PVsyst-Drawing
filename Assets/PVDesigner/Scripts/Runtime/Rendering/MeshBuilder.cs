using System.Collections.Generic;
using PVDesigner.Core.Components;
using PVDesigner.Core.Layout;
using PVDesigner.Core.Model;
using UnityEngine;
using UnityEngine.Rendering;

namespace PVDesigner.Rendering
{
    /// <summary>Small procedural mesh builder with sub-mesh support.</summary>
    public class MeshBuilder
    {
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Vector3> _n = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<Color> _c = new List<Color>();
        private readonly List<List<int>> _tris = new List<List<int>>();

        public MeshBuilder(int subMeshes = 1)
        {
            for (int i = 0; i < subMeshes; i++) _tris.Add(new List<int>());
        }

        public int VertexCount => _v.Count;

        public void AddQuad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal,
            Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Color? color = null)
        {
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            for (int k = 0; k < 4; k++)
            {
                _n.Add(normal);
                _c.Add(color ?? Color.white);
            }
            _uv.Add(ua); _uv.Add(ub); _uv.Add(uc); _uv.Add(ud);
            // In Unity the front face of triangle (a, b, c) faces Cross(b - a, c - a): orient it along the normal.
            var cross = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(cross, normal) >= 0)
            {
                _tris[sub].AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            else
            {
                _tris[sub].AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
        }

        public void AddQuad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color? color = null) =>
            AddQuad(sub, a, b, c, d, normal, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, color);

        /// <summary>Oriented box defined by its centre, three orthonormal axes and half extents.</summary>
        public void AddBox(int sub, Vector3 center, Vector3 ax, Vector3 ay, Vector3 az, Vector3 half, Color? color = null)
        {
            Vector3 x = ax * half.x, y = ay * half.y, z = az * half.z;
            Vector3 P(int sx, int sy, int sz) => center + x * sx + y * sy + z * sz;
            AddQuad(sub, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), ay, color);
            AddQuad(sub, P(-1, -1, -1), P(-1, -1, 1), P(1, -1, 1), P(1, -1, -1), -ay, color);
            AddQuad(sub, P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1), ax, color);
            AddQuad(sub, P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), -ax, color);
            AddQuad(sub, P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1), P(1, -1, 1), az, color);
            AddQuad(sub, P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), -az, color);
        }

        /// <summary>Square beam between two points.</summary>
        public void AddBeam(int sub, Vector3 from, Vector3 to, float thickness, Color? color = null)
        {
            var dir = to - from;
            float len = dir.magnitude;
            if (len < 1e-4f) return;
            dir /= len;
            var side = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
            var up = Vector3.Cross(side, dir).normalized;
            AddBox(sub, (from + to) * 0.5f, side, up, dir, new Vector3(thickness * 0.5f, thickness * 0.5f, len * 0.5f), color);
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (_v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(_v);
            m.SetNormals(_n);
            m.SetUVs(0, _uv);
            m.SetColors(_c);
            m.subMeshCount = _tris.Count;
            for (int i = 0; i < _tris.Count; i++) m.SetTriangles(_tris[i], i);
            m.RecalculateBounds();
            return m;
        }

        // ---------------------------------------------------------------- PV specific meshes

        /// <summary>
        /// Shared mesh of a table in its local frame (X across, Y normal, Z up the slope, origin at the centre).
        /// Sub-mesh 0: module fronts, 1: module backs and structure.
        /// For fixed tables, <paramref name="worldToLocal"/> and <paramref name="centerHeight"/> are used to drop legs to the ground.
        /// </summary>
        public static Mesh BuildTableMesh(ArrayField field, PVModuleSpec module, bool legs, Quaternion worldToLocal, float centerHeight)
        {
            var mb = new MeshBuilder(2);
            TableGeometry.TableSize(field, module, out double wd, out double ld);
            TableGeometry.ModuleSize(field, module, out double ad, out double ud);
            float w = (float)wd, l = (float)ld, a = (float)ad, u = (float)ud, gap = (float)field.ModuleGap;
            const float t = 0.02f;
            bool landscape = field.Orientation == ModuleOrientation.Landscape;

            for (int j = 0; j < field.ModulesUp; j++)
            {
                for (int i = 0; i < field.ModulesAcross; i++)
                {
                    float cx = -w * 0.5f + a * 0.5f + i * (a + gap);
                    float cz = -l * 0.5f + u * 0.5f + j * (u + gap);
                    Vector3 p0 = new Vector3(cx - a * 0.5f, t, cz - u * 0.5f);
                    Vector3 p1 = new Vector3(cx + a * 0.5f, t, cz - u * 0.5f);
                    Vector3 p2 = new Vector3(cx + a * 0.5f, t, cz + u * 0.5f);
                    Vector3 p3 = new Vector3(cx - a * 0.5f, t, cz + u * 0.5f);
                    if (landscape)
                        mb.AddQuad(0, p0, p1, p2, p3, Vector3.up, new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1));
                    else
                        mb.AddQuad(0, p0, p1, p2, p3, Vector3.up, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
                    var down = new Vector3(0, -t, 0);
                    mb.AddQuad(1, p0 + down * 2, p3 + down * 2, p2 + down * 2, p1 + down * 2, Vector3.down);
                }
            }

            // Purlins under the modules.
            float railY = -0.08f;
            foreach (float z in new[] { -l * 0.3f, l * 0.3f })
                mb.AddBox(1, new Vector3(0, railY, z), Vector3.right, Vector3.up, Vector3.forward, new Vector3(w * 0.5f, 0.03f, 0.025f));

            if (legs)
            {
                var down = worldToLocal * Vector3.down;
                int n = Mathf.Max(2, Mathf.CeilToInt(w / 4.0f) + 1);
                for (int k = 0; k < n; k++)
                {
                    float x = -w * 0.5f + 0.3f + (w - 0.6f) * k / (n - 1);
                    foreach (float z in new[] { -l * 0.3f, l * 0.3f })
                    {
                        var top = new Vector3(x, railY - 0.03f, z);
                        // Height of this point above the ground in world space.
                        var localToWorld = Quaternion.Inverse(worldToLocal);
                        float h = centerHeight + (localToWorld * top).y;
                        if (h <= 0.02f) continue;
                        mb.AddBeam(1, top, top + down * h, 0.08f);
                    }
                    // Rafter connecting front and back legs.
                    mb.AddBeam(1, new Vector3(x, railY - 0.06f, -l * 0.45f), new Vector3(x, railY - 0.06f, l * 0.45f), 0.06f);
                }
            }
            return mb.Build("Table");
        }

        /// <summary>Static tracker structure (posts and torque tube). Local X = axis, Y = up, origin at the axis centre.</summary>
        public static Mesh BuildTrackerStructure(float length, float axisHeight)
        {
            var mb = new MeshBuilder(1);
            mb.AddBeam(0, new Vector3(-length * 0.5f, -0.12f, 0), new Vector3(length * 0.5f, -0.12f, 0), 0.12f);
            int n = Mathf.Max(2, Mathf.CeilToInt(length / 7f) + 1);
            for (int k = 0; k < n; k++)
            {
                float x = -length * 0.5f + 0.4f + (length - 0.8f) * k / (n - 1);
                mb.AddBeam(0, new Vector3(x, -0.12f, 0), new Vector3(x, -axisHeight, 0), 0.14f);
            }
            return mb.Build("Tracker structure");
        }

        /// <summary>Flat polygon (XZ plane) with a uniform colour; vertices are given as X / Z pairs.</summary>
        public static Mesh BuildPolygon(IReadOnlyList<Vector2> poly, float y, Color color)
        {
            var pts = new List<PVDesigner.Core.Math.Vec2>(poly.Count);
            foreach (var p in poly) pts.Add(new PVDesigner.Core.Math.Vec2(p.x, p.y));
            var idx = PVDesigner.Core.Math.Polygon2D.Triangulate(pts);
            var m = new Mesh { name = "Polygon" };
            var v = new Vector3[poly.Count];
            var c = new Color[poly.Count];
            var n = new Vector3[poly.Count];
            for (int i = 0; i < poly.Count; i++)
            {
                v[i] = new Vector3(poly[i].x, y, poly[i].y);
                c[i] = color;
                n[i] = Vector3.up;
            }
            // Triangulation is counter-clockwise seen from above; Unity front faces are clockwise.
            var tris = new int[idx.Count];
            for (int i = 0; i < idx.Count; i += 3)
            {
                tris[i] = idx[i];
                tris[i + 1] = idx[i + 2];
                tris[i + 2] = idx[i + 1];
            }
            m.vertices = v;
            m.colors = c;
            m.normals = n;
            m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }
    }
}

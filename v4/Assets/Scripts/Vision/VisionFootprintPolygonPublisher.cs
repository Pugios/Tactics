using System.Collections.Generic;
using UnityEngine;

namespace Tactics.Vision
{
    /// <summary>
    /// Uploads world-space footprint polygon vertices for the fog shader point-in-polygon test.
    /// </summary>
    public static class VisionFootprintPolygonPublisher
    {
        public const int MaxVertices = 64;

        private static readonly Vector4[] ShaderVerts = new Vector4[MaxVertices];

        public static void Publish(IReadOnlyList<Vector3> worldVertices)
        {
            if (worldVertices == null || worldVertices.Count < 3)
            {
                Clear();
                return;
            }

            int count = Mathf.Min(worldVertices.Count, MaxVertices);
            for (int i = 0; i < count; i++)
            {
                Vector3 v = worldVertices[i];
                ShaderVerts[i] = new Vector4(v.x, v.z, 0f, 0f);
            }

            Shader.SetGlobalInt(VisionShaderGlobals.FootprintVertCount, count);
            Shader.SetGlobalVectorArray(VisionShaderGlobals.FootprintVerts, ShaderVerts);
            Shader.SetGlobalFloat(VisionShaderGlobals.HasValidMask, 1f);
        }

        public static void Clear()
        {
            Shader.SetGlobalInt(VisionShaderGlobals.FootprintVertCount, 0);
            Shader.SetGlobalFloat(VisionShaderGlobals.HasValidMask, 0f);
        }
    }
}

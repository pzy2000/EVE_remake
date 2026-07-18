using UnityEngine;

namespace Starfall.Presentation
{
    internal static class CameraRenderQuality
    {
        public static void Configure(Camera camera)
        {
            if (!camera) return;

            camera.allowHDR = true;
            camera.allowMSAA = true;
        }
    }
}

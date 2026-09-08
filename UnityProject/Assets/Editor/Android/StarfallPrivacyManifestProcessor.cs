#if UNITY_EDITOR && UNITY_ANDROID
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace Starfall.Editor
{
    // Unity rewrites its marked GameActivity as exported even when the custom
    // source manifest says false. Enforce the boundary on the generated project.
    public sealed class StarfallPrivacyManifestProcessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => int.MaxValue;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var library = Path.Combine(path, "src/main/AndroidManifest.xml");
            if (!File.Exists(library))
                library = Path.Combine(path, "unityLibrary/src/main/AndroidManifest.xml");
            if (!File.Exists(library)) throw new BuildFailedException("Generated Unity manifest not found.");
            var document = XDocument.Load(library);
            XNamespace android = "http://schemas.android.com/apk/res/android";
            var activities = document.Root?.Element("application")?.Elements("activity").ToArray();
            var gate = activities?.SingleOrDefault(a => (string)a.Attribute(android + "name") ==
                "com.pzy.starfall.mobile.StarfallUnityGameActivity");
            var player = activities?.SingleOrDefault(a => (string)a.Attribute(android + "name") ==
                "com.pzy.starfall.mobile.StarfallUnityPlayerActivity");
            if (gate == null || player == null)
                throw new BuildFailedException("Generated manifest is missing the consent gate or protected player.");
            player.SetAttributeValue(android + "exported", "false");
            player.Elements("intent-filter").Remove();
            gate.SetAttributeValue(android + "exported", "true");
            gate.SetAttributeValue(android + "process", ":privacy");
            gate.Elements("meta-data").Where(m =>
                (string)m.Attribute(android + "name") == "unityplayer.UnityActivity" ||
                (string)m.Attribute(android + "name") == "android.app.lib_name").Remove();
            document.Save(library);
            Debug.Log("[Starfall Privacy] Generated manifest: isolated consent gate; Unity player exported=false.");
        }
    }
}
#endif

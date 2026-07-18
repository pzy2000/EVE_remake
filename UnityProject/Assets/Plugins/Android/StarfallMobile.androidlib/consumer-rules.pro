# Called by name through Unity's AndroidJavaClass bridge.
-keep class com.pzy.starfall.mobile.StarfallMobileBridge { public static *; }

# Referenced from the merged manifest and used only for the one-shot SAF flow.
-keep class com.pzy.starfall.mobile.LegacyDocumentPickerActivity { *; }

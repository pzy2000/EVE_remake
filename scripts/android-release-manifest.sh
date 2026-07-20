#!/usr/bin/env bash

# bundletool emits compiled enum attributes as their integer value, while
# source manifests and some bundletool versions retain the symbolic name.
# Android's SCREEN_ORIENTATION_SENSOR_LANDSCAPE is 6. Unity's Auto Rotation
# with only Landscape Left/Right enabled compiles to USER_LANDSCAPE (11).
starfall_manifest_allows_both_landscape_orientations() {
  local manifest="${1:?missing bundle manifest}"
  grep -Eq 'android:screenOrientation="(sensorLandscape|userLandscape|6|11|0x0*6|0x0*[bB])"' "$manifest"
}

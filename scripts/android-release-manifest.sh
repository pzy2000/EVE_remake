#!/usr/bin/env bash

# bundletool emits compiled enum attributes as their integer value, while
# source manifests and some bundletool versions retain the symbolic name.
# Android's SCREEN_ORIENTATION_SENSOR_LANDSCAPE constant is 6.
starfall_manifest_allows_both_landscape_orientations() {
  local manifest="${1:?missing bundle manifest}"
  grep -Eq 'android:screenOrientation="(sensorLandscape|6|0x0*6)"' "$manifest"
}

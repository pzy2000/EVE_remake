package com.pzy.starfall.mobile;

/** Small JVM-testable generation gate used to invalidate queued Android UI work. */
final class BridgeLifecycleGeneration {
    private int generation;

    synchronized int next() {
        generation++;
        return generation;
    }

    synchronized boolean isCurrent(int candidate) {
        return generation == candidate;
    }
}

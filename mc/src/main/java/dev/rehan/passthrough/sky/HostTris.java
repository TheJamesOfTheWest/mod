// Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
// See LICENSE-SkyCraft in the repository root.
package dev.rehan.passthrough.sky;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ConcurrentHashMap;
import net.minecraft.world.phys.AABB;

/**
 * The host game's ground as exact triangles (Minecraft space), streamed from the host plugin over the WebSocket as binary frames.
 * Triangles are stored per 8x8-block column region; Minecraft's own collision code then runs against them (see SkyCollider).
 */
public final class HostTris {
	public static final int TRI_STAIR_HELPER = 1;
	public static final int TRI_DIGGABLE = 2;
	public static final int TRI_TERRAIN = 8;
	public static final int TRI_MATERIAL_SHIFT = 8;
	public static final int REGION = 8;
	private static final ConcurrentHashMap<Long, SkyTri[]> REGIONS = new ConcurrentHashMap<>();

	private HostTris() {
	}

	private static long key(final int rx, final int rz) {
		return ((long) rx << 32) | (rz & 0xFFFFFFFFL);
	}

	public static boolean active() {
		return !REGIONS.isEmpty();
	}

	public static void clear() {
		REGIONS.clear();
	}

	/** Binary frame: i32 rx, i32 rz, i32 count, then count x (9 floats + i32 flags), little-endian. Replaces that region. */
	public static void ingest(final ByteBuffer frame) {
		ByteBuffer b = frame.order(ByteOrder.LITTLE_ENDIAN);
		int rx = b.getInt(), rz = b.getInt(), count = b.getInt();
		SkyTri[] tris = new SkyTri[count];
		float[] v = new float[9];
		int kept = 0;
		for (int i = 0; i < count; i++) {
			for (int k = 0; k < 9; k++) {
				v[k] = b.getFloat();
			}

			int flags = b.getInt();
			SkyTri t = new SkyTri(v, 0, flags);
			if (!t.degenerate()) {
				tris[kept++] = t;
			}
		}

		REGIONS.put(key(rx, rz), java.util.Arrays.copyOf(tris, kept));
	}

	/** Adds every triangle whose bounds overlap {@code box}. */
	public static void trianglesNear(final AABB box, final List<SkyTri> out) {
		if (REGIONS.isEmpty()) {
			return;
		}

		// a region's triangles reach up to a block and a half past its edge: look one block further out
		int rx0 = Math.floorDiv((int) Math.floor(box.minX - 2.0), REGION), rx1 = Math.floorDiv((int) Math.floor(box.maxX + 2.0), REGION);
		int rz0 = Math.floorDiv((int) Math.floor(box.minZ - 2.0), REGION), rz1 = Math.floorDiv((int) Math.floor(box.maxZ + 2.0), REGION);
		for (int rx = rx0; rx <= rx1; rx++) {
			for (int rz = rz0; rz <= rz1; rz++) {
				SkyTri[] tris = REGIONS.get(key(rx, rz));
				if (tris == null) {
					continue;
				}

				for (SkyTri t : tris) {
					if (t.maxX >= box.minX && t.minX <= box.maxX && t.maxY >= box.minY && t.minY <= box.maxY && t.maxZ >= box.minZ && t.minZ <= box.maxZ) {
						out.add(t);
					}
				}
			}
		}
	}

	public static int triangleCount() {
		int n = 0;
		for (SkyTri[] t : REGIONS.values()) {
			n += t.length;
		}

		return n;
	}
}

// Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
// See LICENSE-SkyCraft in the repository root.
package dev.rehan.passthrough.client;

import dev.rehan.passthrough.sky.HostTris;
import dev.rehan.passthrough.sky.SkyTri;
import dev.rehan.passthrough.sky.TriCollider;
import java.util.ArrayList;
import java.util.List;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;

/** Feeds the local player's movement through TriCollider against the host's nearby ground triangles. */
public final class HostCollider {
	private HostCollider() {
	}

	public static Vec3 collide(final LocalPlayer player, final Vec3 move) {
		AABB box = player.getBoundingBox();
		double step = player.maxUpStep();
		List<SkyTri> tris = new ArrayList<>();
		HostTris.trianglesNear(box.expandTowards(move).inflate(1.0, 1.0 + step, 1.0), tris);
		if (tris.isEmpty()) {
			return move;
		}

		double[] r = TriCollider.resolve(
			tris, (box.minX + box.maxX) * 0.5, box.minY, (box.minZ + box.maxZ) * 0.5, box.getXsize() * 0.5, box.getYsize(), step, player.onGround(),
			move.x, move.y, move.z
		);
		if (r[0] == move.x && r[1] == move.y && r[2] == move.z) {
			return move;
		}

		return Entity.collideBoundingBox(player, new Vec3(r[0], r[1], r[2]), box, player.level(), List.of());
	}
}

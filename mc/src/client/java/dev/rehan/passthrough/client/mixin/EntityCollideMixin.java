// Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
// See LICENSE-SkyCraft in the repository root.
package dev.rehan.passthrough.client.mixin;

import dev.rehan.passthrough.Passthrough;
import dev.rehan.passthrough.client.HostCollider;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.phys.Vec3;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** After vanilla collides the local player's movement with blocks, collide it with the host's exact ground triangles (smooth slopes). */
@Mixin(Entity.class)
abstract class EntityCollideMixin {
	@Inject(method = "collide", at = @At("RETURN"), cancellable = true)
	private void passthrough$smoothGround(final Vec3 movement, final CallbackInfoReturnable<Vec3> cir) {
		if ((Object) this instanceof LocalPlayer player && Passthrough.active && !player.noPhysics) {
			cir.setReturnValue(HostCollider.collide(player, cir.getReturnValue()));
		}
	}
}

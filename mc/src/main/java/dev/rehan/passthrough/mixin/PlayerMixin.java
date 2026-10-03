package dev.rehan.passthrough.mixin;

import dev.rehan.passthrough.Passthrough;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.player.Player;
import org.objectweb.asm.Opcodes;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * The host moves the player (follow and drive modes), so neither side should collide it with blocks: host terrain arrives
 * as barriers that the player can end up inside, and the server would otherwise reject those moves. In walk mode Steve
 * drives himself and must collide with the barriers and the host's triangles (HostCollider), so noclip stays off then.
 */
@Mixin(Player.class)
abstract class PlayerMixin {
	@Inject(method = "tick", at = @At(value = "FIELD", target = "Lnet/minecraft/world/entity/player/Player;noPhysics:Z", opcode = Opcodes.PUTFIELD, shift = At.Shift.AFTER))
	private void passthrough$ghost(final CallbackInfo ci) {
		if (Passthrough.active && !Passthrough.walk) {
			((Entity) (Object) this).noPhysics = true;
		}
	}
}

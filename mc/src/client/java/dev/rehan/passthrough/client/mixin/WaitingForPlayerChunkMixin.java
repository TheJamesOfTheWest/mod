// Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
package dev.rehan.passthrough.client.mixin;

import dev.rehan.passthrough.Passthrough;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * The "Loading terrain" screen waits for the player's chunk section to be compiled for rendering. The host moves the player
 * far away from the world spawn (after joining or a respawn), so don't wait: it would sit on screen and swallow input.
 */
@Mixin(targets = "net.minecraft.client.multiplayer.LevelLoadTracker$WaitingForPlayerChunk")
abstract class WaitingForPlayerChunkMixin {
	@Inject(method = "isReady", at = @At("HEAD"), cancellable = true)
	private void passthrough$ready(final CallbackInfoReturnable<Boolean> cir) {
		if (Passthrough.active) {
			cir.setReturnValue(true);
		}
	}
}

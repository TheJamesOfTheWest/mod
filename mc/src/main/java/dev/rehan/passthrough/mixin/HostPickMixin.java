// Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
// See LICENSE-SkyCraft in the repository root.
package dev.rehan.passthrough.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.rehan.passthrough.sky.SkyClip;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.level.ClipContext;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.BlockHitResult;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/** The crosshair targets the host's ground: a block can be placed on terrain, and the hit points at the cell it would occupy. */
@Mixin(Entity.class)
abstract class HostPickMixin {
	@WrapOperation(
		method = "pick",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/Level;clip(Lnet/minecraft/world/level/ClipContext;)Lnet/minecraft/world/phys/BlockHitResult;")
	)
	private BlockHitResult passthrough$pickHostGround(final Level level, final ClipContext context, final Operation<BlockHitResult> original) {
		return SkyClip.refine(context.getFrom(), context.getTo(), original.call(level, context), SkyClip.Use.PICK);
	}
}

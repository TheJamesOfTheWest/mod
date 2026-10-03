package dev.rehan.passthrough.client.mixin;

import dev.rehan.passthrough.Passthrough;
import net.minecraft.client.Minecraft;
import net.minecraft.client.MouseHandler;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** The host game has the focus, so Minecraft never grabs the mouse; breaking blocks and other held clicks need it to look grabbed. */
@Mixin(MouseHandler.class)
abstract class MouseHandlerMixin {
	@Inject(method = "isMouseGrabbed", at = @At("HEAD"), cancellable = true)
	private void passthrough$grabbedWhileHosted(final CallbackInfoReturnable<Boolean> cir) {
		if (Passthrough.active && Minecraft.getInstance().gui.screen() == null) {
			cir.setReturnValue(true);
		}
	}
}

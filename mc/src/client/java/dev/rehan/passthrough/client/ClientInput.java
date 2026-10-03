package dev.rehan.passthrough.client;

import com.google.gson.JsonObject;
import dev.rehan.passthrough.Passthrough;
import dev.rehan.passthrough.client.mixin.KeyMappingAccessor;
import net.minecraft.client.KeyMapping;
import net.minecraft.client.Minecraft;
import dev.rehan.passthrough.client.mixin.MouseHandlerAccessor;
import net.minecraft.client.input.CharacterEvent;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.MouseButtonInfo;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.world.entity.player.Inventory;
import org.lwjgl.sdl.SDLVideo;

/** Host input, applied on the client thread: the host window has the focus, so Minecraft never sees these itself. */
final class ClientInput {
	/** The host's virtual cursor while a screen (inventory) is open, in GUI-scaled coordinates. */
	private static double cursorX;
	private static double cursorY;

	private ClientInput() {
	}

	static double cursorX() {
		return cursorX;
	}

	static double cursorY() {
		return cursorY;
	}

	static void handle(final Minecraft minecraft, final JsonObject m) {
		LocalPlayer player = minecraft.player;
		switch (m.get("t").getAsString()) {
			case "key" -> {
				String k = m.get("k").getAsString();
				boolean down = !m.has("down") || m.get("down").getAsBoolean();
				if (k.equals("escape")) {
					if (down && minecraft.gui.screen() != null) {
						minecraft.gui.screen().onClose();
					}

					return;
				}

				KeyMapping key = switch (k) {
					case "use" -> minecraft.options.keyUse;
					case "attack" -> minecraft.options.keyAttack;
					case "pick" -> minecraft.options.keyPickItem;
					case "inventory" -> minecraft.options.keyInventory;
					case "drop" -> minecraft.options.keyDrop;
					case "swap" -> minecraft.options.keySwapOffhand;
					case "forward" -> minecraft.options.keyUp;
					case "back" -> minecraft.options.keyDown;
					case "left" -> minecraft.options.keyLeft;
					case "right" -> minecraft.options.keyRight;
					case "jump" -> minecraft.options.keyJump;
					case "sneak" -> minecraft.options.keyShift;
					case "sprint" -> minecraft.options.keySprint;
					default -> null;
				};
				if (k.equals("attack") && down && player != null
					&& BuiltInRegistries.ITEM.getKey(player.getMainHandItem().getItem()).getPath().endsWith("_sword")) {
					// a sword swing: the host hits what's in front of Steve in its own world
					Passthrough.events.accept("{\"t\":\"melee\"}");
				}

				if (key != null) {
					if (down && !key.isDown()) {
						KeyMappingAccessor access = (KeyMappingAccessor)key;
						access.passthrough$setClickCount(access.passthrough$getClickCount() + 1);
					}

					key.setDown(down);
				}
			}
			case "slot" -> {
				if (player != null) {
					player.getInventory().setSelectedSlot(Math.clamp(m.get("n").getAsInt(), 0, Inventory.getSelectionSize() - 1));
				}
			}
			case "scroll" -> {
				if (player != null) {
					Inventory inventory = player.getInventory();
					int size = Inventory.getSelectionSize();
					inventory.setSelectedSlot(Math.floorMod(inventory.getSelectedSlot() - m.get("d").getAsInt(), size));
				}
			}
			case "mouse" -> {
				// {"t":"mouse","x":px,"y":px}: the host's virtual cursor in Minecraft-window pixels, applied only while a screen is open
				if (minecraft.gui.screen() != null) {
					double scale = Math.max(1, minecraft.getWindow().getGuiScale());
					double x = m.get("x").getAsDouble() / scale, y = m.get("y").getAsDouble() / scale;
					double dx = x - cursorX, dy = y - cursorY;
					cursorX = x;
					cursorY = y;
					((MouseHandlerAccessor) minecraft.mouseHandler).passthrough$setXpos(m.get("x").getAsDouble());
					((MouseHandlerAccessor) minecraft.mouseHandler).passthrough$setYpos(m.get("y").getAsDouble());
					minecraft.gui.screen().mouseMoved(x, y);
					if (minecraft.mouseHandler.isLeftPressed()) {
						minecraft.gui.screen().mouseDragged(new MouseButtonEvent(x, y, new MouseButtonInfo(0, 0)), dx, dy);
					}
				}
			}
			case "click" -> {
				// {"t":"click","b":0 left | 1 right | 2 middle,"down":bool}
				Passthrough.LOG.info("host click b={} down={} at ({},{}) screen={}", m.get("b").getAsInt(), m.get("down").getAsBoolean(), cursorX, cursorY,
					minecraft.gui.screen() == null ? "none" : minecraft.gui.screen().getClass().getSimpleName());
				if (minecraft.gui.screen() != null) {
					MouseButtonEvent event = new MouseButtonEvent(cursorX, cursorY, new MouseButtonInfo(m.get("b").getAsInt(), 0));
					if (m.get("down").getAsBoolean()) {
						minecraft.gui.screen().mouseClicked(event, false);
					} else {
						minecraft.gui.screen().mouseReleased(event);
					}
				}
			}
			case "skey" -> {
				// {"t":"skey","sc":SDL scancode,"mods":SDL modifier mask}: a key press for the open screen (search box, shortcuts, closing it)
				if (minecraft.gui.screen() != null) {
					int sc = m.get("sc").getAsInt();
					minecraft.gui.screen().keyPressed(new KeyEvent(sc, sc, m.has("mods") ? m.get("mods").getAsInt() : 0));
				}
			}
			case "char" -> {
				// {"t":"char","c":unicode code point}: typed text for the open screen
				if (minecraft.gui.screen() != null) {
					minecraft.gui.screen().charTyped(new CharacterEvent(m.get("c").getAsInt()));
				}
			}
			case "mscroll" -> {
				if (minecraft.gui.screen() != null) {
					minecraft.gui.screen().mouseScrolled(cursorX, cursorY, 0.0, m.get("d").getAsDouble());
				}
			}
			case "setpos" -> {
				// {"t":"setpos","p":[x,y,z]}: put the player back on the ground (Steve fell into a barrier or the void)
				if (player != null) {
					com.google.gson.JsonArray a = m.getAsJsonArray("p");
					player.setPos(a.get(0).getAsDouble(), a.get(1).getAsDouble(), a.get(2).getAsDouble());
					player.setDeltaMovement(net.minecraft.world.phys.Vec3.ZERO);
				}
			}
			case "hud" -> {
				if (minecraft.gui.hud.isHidden() != m.get("hidden").getAsBoolean()) {
					minecraft.gui.hud.toggle();
				}
			}
			case "view" -> {
				// match the host's picture exactly: un-minimize/un-maximize first (resizing a maximized window is ignored)
				int w = m.get("w").getAsInt(), h = m.get("h").getAsInt();
				long handle = minecraft.getWindow().handle();
				SDLVideo.SDL_RestoreWindow(handle);
				minecraft.getWindow().setWindowed(w, h);
				SDLVideo.SDL_SetWindowSize(handle, w, h);
				SDLVideo.SDL_SyncWindow(handle);
			}
			default -> {
			}
		}
	}
}

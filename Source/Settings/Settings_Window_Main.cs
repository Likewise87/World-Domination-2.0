using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class WorldDominationMod : Mod
    {
        public static WorldDominationSettings settings;
        private static WorldDominationMod instance;

        public WorldDominationMod(ModContentPack content) : base(content)
        {
            instance = this;
            settings = GetSettings<WorldDominationSettings>();
        }

        /// <summary>Writes mod settings to disk (Config folder). Use after changing lastSeenReleaseNotesVersion so dismiss persists across saves/worlds.</summary>
        public static void SaveSettingsToDisk()
        {
            instance?.WriteSettings();
        }

        /// <summary>Opens this mod's settings hub (same UI as Options → Mod settings).</summary>
        public static void OpenModSettingsWindow()
        {
            Mod mod = instance ?? LoadedModManager.GetMod<WorldDominationMod>();
            if (mod == null) return;
            Find.WindowStack.Add(new Dialog_ModSettings(mod));
        }

        /// <summary>Open mod settings focused on a specific page (used by deep-links).</summary>
        public static void OpenModSettingsWindow(WdSettingsPage page)
        {
            WdSettingsShell.FocusPage(page);
            OpenModSettingsWindow();
        }

        /// <summary>Unified allegiance editor needs a loaded game (faction list). Block from main menu mod settings.</summary>
        public static void TryOpenAllegianceLockWindow()
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                Messages.Message("TSA_WD_AllegianceMatrixInGameOnly".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            Find.WindowStack.Add(new Dialog_WdWorldGenAllegiances());
        }

        public override string SettingsCategory() => "TSA_WD_Category".Translate();

        /// <summary>
        /// Flat settings-window fill (overview --bg-panel tone #0e121c). Slightly blue so it reads apart from pure black
        /// and matches the previous nested-panel look as one coherent surface.
        /// </summary>
        public static readonly Color SettingsPageBg = new Color(14f / 255f, 18f / 255f, 28f / 255f, 1f);

        /// <summary>Same outline as slate buttons (<see cref="WorldDomination_UIUtils"/> slate chrome).</summary>
        public static readonly Color SettingsPageBorder = new Color(0.55f, 0.62f, 0.72f, 0.42f);

        /// <summary>Inner padding once we own the full settings window surface.</summary>
        public const float SettingsPagePad = 10f;

        /// <summary>
        /// Draws the WD 2.0 wordmark top-left inside <paramref name="rect"/> (aspect-fitted, left-aligned).
        /// Returns the height used (0 when texture missing).
        /// </summary>
        public static float DrawSettingsBrandTitleTopLeft(Rect rect, float height = 40f)
        {
            Texture2D tex = WdSettingsChromeAssets.BrandTitle;
            if (tex == null) return 0f;
            float aspect = tex.width / (float)Mathf.Max(1, tex.height);
            float drawH = Mathf.Min(height, rect.height);
            float drawW = Mathf.Min(rect.width, drawH * aspect);
            Widgets.DrawTextureFitted(new Rect(rect.x, rect.y, drawW, drawH), tex, 1f);
            return drawH;
        }

        /// <summary>Draws the WD 2.0 wordmark fitted into <paramref name="rect"/>. No-op if the texture is missing.</summary>
        public static void DrawSettingsBrandTitle(Rect rect)
        {
            if (WdSettingsChromeAssets.BrandTitle == null) return;
            Widgets.DrawTextureFitted(rect, WdSettingsChromeAssets.BrandTitle, 1f);
        }

        /// <summary>After settings are saved (window closed), invalidate cached derived values so changes like attack range and escalation take effect immediately in-game.</summary>
        public override void WriteSettings()
        {
            base.WriteSettings();
            WdSettingsShell.OnSettingsWritten();
            if (Current.ProgramState != ProgramState.Playing) return;
            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.NotifyInfluenceSettingsChanged();
            WorldComponent_InterceptionScheduler.Current?.NotifyAtTurretRangeSettingsChanged();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Fallback if InitialSize patch missed the host field name: grow once while we draw.
            EnsureHostWindowExtraSize();

            // Hauler's Dream chrome takeover: suppress vanilla corner X + bottom Close (save still runs on
            // PreClose), draw our own padded X, reclaim the close-button strip, and keep one flat fill.
            Window win = Find.WindowStack?.currentlyDrawnWindow;
            if (win != null)
            {
                win.draggable = true;
                win.doCloseButton = false;
                win.doCloseX = false;
            }

            // DoSettingsWindowContents is handed a rect below the vanilla SettingsCategory label (y≈40).
            // Overdraw from y=0 so the redundant "World Domination" text never shows; extend past yMax to
            // cover the strip the bottom Close button used to occupy.
            float fullW = inRect.width;
            float fillBottom = inRect.yMax + 32f;
            Widgets.DrawBoxSolid(new Rect(0f, 0f, fullW, fillBottom), SettingsPageBg);

            const float pad = SettingsPagePad;
            const float bandH = 52f;
            const float brandH = 40f;
            float innerW = Mathf.Max(1f, fullW - pad * 2f);
            float brandY = pad + (bandH - brandH) * 0.5f;

            // Leave the top-right clear for the padded close X.
            DrawSettingsBrandTitleTopLeft(
                new Rect(pad, brandY, innerW - 36f, brandH), brandH);

            if (Widgets.CloseButtonFor(new Rect(pad, pad + 6f, innerW - 6f, 40f)))
                win?.Close();

            float bodyTop = pad + bandH + 6f;
            Rect shellRect = new Rect(pad, bodyTop, innerW, Mathf.Max(100f, fillBottom - pad - bodyTop));
            WdSettingsShell.Draw(shellRect);

            // Slate outline on the owned page surface (drawn last so content cannot cover it).
            // Dialog Postfix also strokes the outer ImmediateWindow rect.
            Color prev = GUI.color;
            GUI.color = SettingsPageBorder;
            Widgets.DrawBox(new Rect(0f, 0f, fullW, fillBottom), 1);
            GUI.color = prev;
        }

        private static Dialog_ModSettings widenedHost;

        private static void EnsureHostWindowExtraSize()
        {
            Dialog_ModSettings host = Find.WindowStack?.WindowOfType<Dialog_ModSettings>();
            if (host == null || ReferenceEquals(widenedHost, host)) return;

            // InitialSize postfix already applied → window is already sized; just remember it.
            // If not, grow the live rect (next frames use the larger area via Window layout).
            const float extraW = Patch_Dialog_ModSettings_Width.ExtraWidth;
            const float extraH = Patch_Dialog_ModSettings_Width.ExtraHeight;
            Rect r = host.windowRect;
            bool changed = false;
            // Heuristic: vanilla host is typically ≤ ~920; if already expanded, skip width.
            if (r.width < 1000f)
            {
                r.width += extraW;
                r.x -= extraW * 0.5f;
                if (r.x < 0f) r.x = 0f;
                if (r.xMax > UI.screenWidth)
                    r.x = Mathf.Max(0f, UI.screenWidth - r.width);
                changed = true;
            }
            // Vanilla host height is typically ~700; bump once if the InitialSize patch missed.
            if (r.height < 780f)
            {
                r.height += extraH;
                r.y -= extraH * 0.5f;
                if (r.y < 0f) r.y = 0f;
                if (r.yMax > UI.screenHeight)
                    r.y = Mathf.Max(0f, UI.screenHeight - r.height);
                changed = true;
            }
            if (changed)
                host.windowRect = r;
            widenedHost = host;
        }
    }
}

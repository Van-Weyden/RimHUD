﻿using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimHUD.Compatibility.Multiplayer;
using RimHUD.Configuration;
using RimHUD.Engine;
using RimHUD.Interface.Hud.Layout;
using RimHUD.Interface.Hud.Models;
using RimHUD.Patch;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimHUD.Interface
{
  public static class InspectPanePlus
  {
    private const int LogLinesMax = 300;

    private const float ButtonSize = 24f;

    private static Vector2 _scrollPosition = Vector2.zero;

    private static List<ITab_Pawn_Log_Utility.LogLineDisplayable> _log;
    private static ITab_Pawn_Log_Utility.LogDrawData _logDrawData = new ITab_Pawn_Log_Utility.LogDrawData();

    private static int _lastBattleTick = -1;
    private static int _lastPlayTick = -1;
    private static Pawn _pawn;

    private static readonly Dictionary<InspectTabBase, int> TabButtonWidths = new Dictionary<InspectTabBase, int>();

    public static void OnGUI(Rect rect, IInspectPane pane)
    {
      Theme.CheckFontChange();

      var selected = PawnModel.Selected;

      pane.RecentHeight = Theme.InspectPaneHeight.Value - 35f;

      if (selected == null) { return; }
      if (!pane.AnythingSelected) { return; }

      var bounds = GetContentRect(rect);

      try
      {
        GUI.BeginGroup(bounds.ExpandedBy(1f));

        var lineEndWidth = 0f;

        if (pane.ShouldShowSelectNextInCellButton)
        {
          var selectOverlappingNextRect = new Rect(bounds.width - ButtonSize, 0f, ButtonSize, ButtonSize);
          if (Widgets.ButtonImage(selectOverlappingNextRect, Textures.SelectOverlappingNext)) { pane.SelectNextInCell(); }
          lineEndWidth += ButtonSize;
          TooltipHandler.TipRegion(selectOverlappingNextRect, "SelectNextInSquareTip".Translate(KeyBindingDefOf.SelectNextInCell.MainKeyLabel));
        }

        DrawButtons(bounds, ref lineEndWidth);

        var previousAnchor = Text.Anchor;
        Text.Anchor = TextAnchor.UpperLeft;
        GUIPlus.SetFont(GameFont.Medium);
        GUIPlus.SetColor(selected.FactionRelationColor);

        var labelRect = new Rect(0f, 0f, bounds.width - lineEndWidth, Text.LineHeight);
        var label = selected.NameText;

        Widgets.Label(labelRect, label);
        if (Widgets.ButtonInvisible(labelRect)) { ToggleSocialTab(); }

        GUIPlus.ResetFont();
        GUIPlus.ResetColor();
        Text.Anchor = previousAnchor;
        WidgetsPlus.DrawTooltip(labelRect, selected.BioTooltip, true);

        if (!pane.ShouldShowPaneContents) { return; }

        var contentRect = bounds.AtZero();
        contentRect.yMin += 26f;
        DrawContent(contentRect, selected, null);
      }
      catch (Exception exception) { Troubleshooter.HandleError(exception); }
      finally { GUI.EndGroup(); }

      if (!Persistent.TutorialComplete) { Tutorial.DoInspectPane(rect); }
    }

    public static Rect GetContentRect(Rect bounds)
    {
      var rect = bounds.ContractedBy(12f);
      rect.yMin -= 4f;
      rect.yMax += 6f;

      return rect;
    }

    public static void DrawContent(Rect rect, PawnModel selected, Pawn pawn)
    {
      if (pawn == null)
      {
        if (selected == null) { throw new Mod.Exception("Both model and pawn are null"); }

        pawn = selected.Base;
      }

      Text.Font = GameFont.Small;

      if (Theme.HudDocked.Value) { HudLayout.DrawDocked(rect, selected); }
      else if (Theme.InspectPaneTabAddLog.Value) { DrawLog(pawn, rect); }
    }

    public static bool DrawTabs(IInspectPane pane)
    {
      if (pane.CurTabs == null) { return false; }

      try
      {
        var y = pane.PaneTopY - 30f;
        var x = InspectPaneUtility.PaneWidthFor(pane) - Theme.InspectPaneTabWidth.Value;

        var width = 0f;
        var isSelected = false;

        foreach (var tab in pane.CurTabs)
        {
          var isOpenTab = tab.GetType() == pane.OpenTabType;

          if (isOpenTab)
          {
            tab.DoTabGUI();
            pane.RecentHeight = 700f;
            isSelected = true;
          }

          if (!tab.IsVisible || tab.Hidden) { continue; }

          int buttonWidth;
          if (TabButtonWidths.TryGetValue(tab, out var tabButtonWidth)) { buttonWidth = tabButtonWidth; }
          else
          {
            buttonWidth = Traverse.Create(tab).Field<int>("tabButtonWidth")?.Value ?? 0;
            if (buttonWidth <= 0) { buttonWidth = Theme.InspectPaneTabWidth.Value; }
            TabButtonWidths[tab] = buttonWidth;
          }

          var rect = new Rect(x, y, buttonWidth, 30f);
          width = x;

          Text.Font = GameFont.Small;

          if (Widgets.ButtonText(rect, tab.labelKey.Translate())) { InterfaceToggleTab(tab, pane); }

          if (!isOpenTab && !tab.TutorHighlightTagClosed.NullOrEmpty()) { UIHighlighter.HighlightOpportunity(rect, tab.TutorHighlightTagClosed); }

          x -= Theme.InspectPaneTabWidth.Value;
        }
        if (!isSelected) { return false; }

        GUI.DrawTexture(new Rect(0f, y, width, 30f), Textures.InspectTabButtonFill);
      }
      catch (Exception exception) { Troubleshooter.HandleError(exception); }
      return false;
    }

    private static void DrawLog(Pawn pawn, Rect rect)
    {
      if (_log == null || _lastBattleTick != pawn.records.LastBattleTick || _lastPlayTick != Find.PlayLog.LastTick || _pawn != pawn)
      {
        ClearCache();
        _log = ITab_Pawn_Log_Utility.GenerateLogLinesFor(pawn, true, true, true, LogLinesMax).ToList();
        _lastPlayTick = Find.PlayLog.LastTick;
        _lastBattleTick = pawn.records.LastBattleTick;
        _pawn = pawn;
      }

      var width = rect.width - WidgetsPlus.ScrollbarWidth;
      var height = _log.Sum(line => line.GetHeight(rect.width));

      if (height <= 0f) { return; }

      var viewRect = new Rect(0f, 0f, rect.width - WidgetsPlus.ScrollbarWidth, height);

      _logDrawData.StartNewDraw();

      Widgets.BeginScrollView(rect, ref _scrollPosition, viewRect);

      var y = 0f;
      foreach (var line in _log.OfType<ITab_Pawn_Log_Utility.LogLineDisplayableLog>())
      {
        line.Draw(y, width, _logDrawData);
        y += line.GetHeight(width);
      }

      Widgets.EndScrollView();
    }

    private static void DrawButtons(Rect rect, ref float lineEndWidth)
    {
      if (Find.Selector.NumSelected != 1) { return; }
      var selected = Find.Selector.SingleSelectedThing;
      if (selected == null) { return; }

      lineEndWidth += ButtonSize;
      Widgets.InfoCardButton(rect.width - lineEndWidth, 0f, selected);

      if (!(selected is Pawn pawn)) { return; }

      if (HudContent.AllowXenotypeButton(pawn)) { DrawXenotypeButton(pawn, GetRowButtonRect(rect, ref lineEndWidth)); }

      if (HudContent.AllowFactionIconButton(pawn)) { FactionUIUtility.DrawFactionIconWithTooltip(GetRowButtonRect(rect, ref lineEndWidth), pawn.Faction); }

      if (HudContent.AllowIdeoButton(pawn)) { DrawIdeoligionButton(pawn, GetRowButtonRect(rect, ref lineEndWidth)); }

      if (!IsPlayerControlled(pawn)) { return; }

      lineEndWidth += WidgetsPlus.SmallPadding;

      if (HudContent.AllowResponseButton(pawn)) { HostilityResponseModeUtility.DrawResponseButton(GetRowButtonRect(rect, ref lineEndWidth), pawn, false); }

      if (HudContent.AllowMedicalButton(pawn)) { DrawMedicalButton(pawn, GetRowButtonRect(rect, ref lineEndWidth)); }
      if (HudContent.AllowRenameButton(pawn)) { TrainingCardUtility.DrawRenameButton(GetRowButtonRect(rect, ref lineEndWidth, 4f, 4f), pawn); }

      if (HudContent.AllowSelfTendButton(pawn)) { DrawSelfTendButton(pawn, GetRowButtonRect(rect, ref lineEndWidth)); }
    }

    private static Rect GetRowButtonRect(Rect bounds, ref float lineEndWidth, float widthModifier = 0f, float heightModifier = 0f)
    {
      lineEndWidth += ButtonSize;
      var rect = new Rect(bounds.width - lineEndWidth, 0f, ButtonSize + widthModifier, ButtonSize + heightModifier);
      lineEndWidth += WidgetsPlus.SmallPadding;
      return rect;
    }

    private static bool IsPlayerControlled(Pawn pawn) => !pawn.Dead && pawn.playerSettings != null && ((pawn.Faction?.IsPlayer ?? false) || (pawn.HostFaction?.IsPlayer ?? false));

    private static void DrawSelfTendButton(Pawn pawn, Rect rect)
    {
      var canDoctor = !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor);
      var canDoctorPriority = pawn.workSettings == null || pawn.workSettings?.GetPriority(WorkTypeDefOf.Doctor) > 0;

      var selfTendTip = "SelfTendTip".Translate(Faction.OfPlayer.def.pawnsPlural, 0.7f.ToStringPercent()).CapitalizeFirst();

      if (!canDoctor) { selfTendTip += "\n\n" + "MessageCannotSelfTendEver".Translate(pawn.LabelShort, pawn); }
      else if (!canDoctorPriority) { selfTendTip += "\n\n" + "MessageSelfTendUnsatisfied".Translate(pawn.LabelShort, pawn); }

      var selfTend = pawn.playerSettings.selfTend;
      selfTend = WidgetsPlus.DrawToggle(rect, selfTend, new TipSignal(selfTendTip, WidgetsPlus.StandardTooltipId), canDoctor, Textures.SelfTendOnIcon, Textures.SelfTendOffIcon);
      if (selfTend != pawn.playerSettings.selfTend) { Mod_Multiplayer.SetSelfTend(pawn, selfTend); }
    }

    private static void DrawMedicalButton(Pawn pawn, Rect rect)
    {
      MedicalCareUtility.MedicalCareSelectButton(rect, pawn);
      WidgetsPlus.DrawTooltip(rect, () => Lang.Get("Model.Health.MedicalCare", pawn.KindLabel, pawn.playerSettings.medCare.GetLabel()));
    }

    private static void DrawIdeoligionButton(Pawn pawn, Rect rect)
    {
      IdeoUIUtility.DoIdeoIcon(rect, pawn.Ideo, false, () => IdeoUIUtility.OpenIdeoInfo(pawn.Ideo));
      if (!Mouse.IsOver(rect)) { return; }

      var name = pawn.ideo.Ideo.name.Colorize(ColoredText.TipSectionTitleColor);
      var certainty = "Certainty".Translate().CapitalizeFirst().Resolve() + ": " + pawn.ideo.Certainty.ToStringPercent();
      var previous = pawn != null && pawn.ideo.PreviousIdeos.Any() ? "\n\n" + "Formerly".Translate().CapitalizeFirst() + ": \n" + (from x in pawn.ideo.PreviousIdeos select x.name).ToLineList("  - ") : null;
      TooltipHandler.TipRegion(rect, $"{name}\n{certainty}{previous}");
    }

    private static void DrawXenotypeButton(Pawn pawn, Rect rect)
    {
      if (Mouse.IsOver(rect)) { Widgets.DrawHighlight(rect); }

      if (Widgets.ButtonImage(rect, pawn.genes.XenotypeIcon)) { InspectPaneUtility.OpenTab(typeof(ITab_Genes)); }

      if (!Mouse.IsOver(rect)) { return; }

      var tooltip = ("Xenotype".Translate().CapitalizeFirst().Resolve() + ": " + pawn.genes.XenotypeLabelCap).Colorize(ColoredText.TipSectionTitleColor);
      var stage = pawn.ageTracker?.CurLifeStage?.LabelCap.ToString();

      if (stage != null) { tooltip += "\n" + Lang.Get("Model.Bio.Lifestage", stage); }
      if (!pawn.genes.Xenotype.description.NullOrEmpty()) { tooltip += "\n\n" + pawn.genes.Xenotype.description; }

      TooltipHandler.TipRegion(rect, tooltip);
    }

    public static void ClearButtonWidths() => TabButtonWidths.Clear();

    public static void ClearCache()
    {
      _log = null;
      _pawn = null;
      _lastBattleTick = -1;
      _lastPlayTick = -1;
      _logDrawData = new ITab_Pawn_Log_Utility.LogDrawData();
      _scrollPosition = Vector2.zero;
      TabButtonWidths.Clear();
    }

    private static void InterfaceToggleTab(InspectTabBase tab, IInspectPane pane) => Access.Method_RimWorld_InspectPaneUtility_InterfaceToggleTab.Invoke(null, tab, pane);

    private static void ToggleTab(Type tabType)
    {
      var pane = (MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow;
      var tab = (from t in pane.CurTabs where tabType.IsInstanceOfType(t) select t).FirstOrDefault();
      if (tab == null) { return; }

      if (Find.MainTabsRoot.OpenTab != MainButtonDefOf.Inspect) { Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect); }

      Access.Method_RimWorld_InspectPaneUtility_ToggleTab.Invoke(null, tab, pane);
    }

    public static void ToggleBioTab() => ToggleTab(typeof(ITab_Pawn_Character));
    public static void ToggleGearTab() => ToggleTab(typeof(ITab_Pawn_Gear));
    public static void ToggleHealthTab() => ToggleTab(typeof(ITab_Pawn_Health));
    public static void ToggleNeedsTab() => ToggleTab(typeof(ITab_Pawn_Needs));
    public static void ToggleSocialTab() => ToggleTab(typeof(ITab_Pawn_Social));
    public static void ToggleTrainingTab() => ToggleTab(typeof(ITab_Pawn_Training));
    public static void TogglePrisonerTab() => ToggleTab(typeof(ITab_Pawn_Prisoner));
    public static void ToggleSlaveTab() => ToggleTab(typeof(ITab_Pawn_Slave));
  }
}

using AutoOpen.Utils;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Vector2 = System.Numerics.Vector2;

namespace AutoOpen;

public class AutoOpen : BaseSettingsPlugin<Settings>
{
    private IngameState IngameState => GameController.Game.IngameState;
    private Vector2 WindowOffset => GameController.Window.GetWindowRectangleTimeCache.TopLeft.ToVector2Num();
    private readonly Dictionary<long, int> _clickedEntities = [];
    private List<string> _doorBlacklist;
    private List<string> _switchBlacklist;
    private List<string> _chestWhitelist;

    public override bool Initialise()
    {
        _doorBlacklist = LoadFile("doorBlacklist.txt");
        _switchBlacklist = LoadFile("switchBlacklist.txt");
        _chestWhitelist = LoadFile("chestWhitelist.txt");
        return true;
    }

    public override void Render()
    {
        var toggleHotkeyPressed = Settings.ToggleEntityKey.PressedOnce();
        var camera = IngameState.Camera;
        var playerPos = GameController.Player.PosNum;
        var prevMousePosition = Input.ForceMousePositionNum;

        if (!new[] { Settings.DoorSettings, Settings.ChestSettings, Settings.ShrineSettings, Settings.SwitchSettings }.Any(x => x.Open))
        {
            return;
        }

        var entities = GameController.EntityListWrapper.OnlyValidEntities
            .Where(entity => entity.HasComponent<Render>() &&
                             entity.Address != GameController.Player.Address &&
                             entity.IsValid &&
                             entity.IsTargetable &&
                             !entity.HasComponent<Monster>() &&
                             (entity.HasComponent<TriggerableBlockage>() ||
                              entity.HasComponent<Transitionable>() ||
                              entity.HasComponent<Chest>() ||
                              entity.HasComponent<Shrine>() ||
                              entity.Path.Contains("darkshrine", StringComparison.OrdinalIgnoreCase)));

        foreach (var entity in entities)
        {
            var entityPos = entity.PosNum;
            var entityDistanceToPlayer = (playerPos - entityPos).Length();
            if (!entity.TryGetComponent<Targetable>(out var targetableComp))
            {
                _clickedEntities.Remove(entity.Address);
                continue;
            }

            var entityScreenPos = camera.WorldToScreen(entityPos);
            var isTargeted = targetableComp.isTargeted;

            if (Settings.DoorSettings.Open &&
                entity.HasComponent<TriggerableBlockage>() &&
                entity.Path.Contains("door", StringComparison.OrdinalIgnoreCase) &&
                !entity.Path.Contains("door_npc", StringComparison.OrdinalIgnoreCase))
            {
                var isClosed = entity.GetComponent<TriggerableBlockage>().IsClosed;

                var isBlacklisted = _doorBlacklist != null && _doorBlacklist.Contains(entity.Path);
                if (!isBlacklisted)
                    Graphics.DrawText(isClosed ? "closed" : "opened",
                        entityScreenPos,
                        isClosed ? Color.Red : Color.Green,
                        FontAlign.Center);

                if (isTargeted && toggleHotkeyPressed)
                    ToggleDoorBlacklistItem(entity.Path);

                if (!isBlacklisted && isClosed && Control.MouseButtons == MouseButtons.Left)
                {
                    Open(entity, entityDistanceToPlayer, entityScreenPos, prevMousePosition, Settings.DoorSettings.MaxDistance);
                }
            }

            if (Settings.SwitchSettings.Open &&
                entity.HasComponent<Transitionable>() &&
                !entity.HasComponent<TriggerableBlockage>() &&
                entity.Path.Contains("switch", StringComparison.OrdinalIgnoreCase))
            {
                var isBlacklisted = _switchBlacklist != null && _switchBlacklist.Contains(entity.Path);
                var switchState = entity.GetComponent<Transitionable>().Flag1;
                var switched = switchState != 1;

                if (!isBlacklisted)
                {
                    var count = 1;
                    Graphics.DrawText(isTargeted ? "targeted" : "not targeted",
                        entityScreenPos.Translate(0, count * 16),
                        isTargeted ? Color.Green : Color.Red,
                        FontAlign.Center);
                    count++;
                    Graphics.DrawText($"{(switched ? "switched" : "not switched")}:{switchState}",
                        entityScreenPos.Translate(0, count * 16),
                        switched ? Color.Green : Color.Red,
                        FontAlign.Center);
                    count++;
                }

                if (isTargeted && toggleHotkeyPressed)
                    ToggleSwitchBlacklistItem(entity.Path);

                if (!isBlacklisted && !switched && Control.MouseButtons == MouseButtons.Left)
                {
                    Open(entity, entityDistanceToPlayer, entityScreenPos, prevMousePosition, Settings.SwitchSettings.MaxDistance);
                }
            }

            if (Settings.ChestSettings.Open &&
                (entity.HasComponent<Chest>() ||
                 entity.Path.Contains("chest", StringComparison.OrdinalIgnoreCase)))
            {
                var isOpened = entity.GetComponent<Chest>().IsOpened;
                var whitelisted = _chestWhitelist != null && _chestWhitelist.Contains(entity.Path);

                if (isTargeted && toggleHotkeyPressed)
                    ToggleChestWhitelistItem(entity.Path);

                if (whitelisted && !isOpened)
                {
                    Graphics.DrawText("Open me!", entityScreenPos, Color.LimeGreen, FontAlign.Center);
                    if (Control.MouseButtons == MouseButtons.Left)
                    {
                        Open(entity, entityDistanceToPlayer, entityScreenPos, prevMousePosition, Settings.ChestSettings.MaxDistance);
                    }
                }
            }

            if (Settings.ShrineSettings.Open &&
                (entity.HasComponent<Shrine>() ||
                 entity.Path.Contains("darkshrine", StringComparison.OrdinalIgnoreCase)))
            {
                Graphics.DrawText("Get me!", entityScreenPos, Color.LimeGreen, FontAlign.Center);
                if (Control.MouseButtons == MouseButtons.Left)
                {
                    Open(entity, entityDistanceToPlayer, entityScreenPos, prevMousePosition, Settings.ShrineSettings.MaxDistance);
                }
            }
        }
    }

    private void Open(Entity entity, float entityDistanceToPlayer, Vector2 entityScreenPos, Vector2 prevMousePosition, double maxDistance)
    {
        if (entityDistanceToPlayer <= maxDistance)
        {
            if (GetEntityClickedCount(entity) <= 15)
            {
                if (Settings.UseMagicInput)
                {
                    Mouse.LeftUp(0);
                    GameController.PluginBridge.GetMethod<Action<Entity, uint>>("MagicInput.CastSkillWithTarget")(entity, 0x400);
                    Mouse.LeftDown(0);
                    _clickedEntities[entity.Address] = _clickedEntities.GetValueOrDefault(entity.Address) + 1;
                }
                else
                {
                    if (Settings.BlockInputWhenClicking) Mouse.blockInput(true);
                    try
                    {
                        Mouse.MoveMouse(entityScreenPos + WindowOffset);
                        Mouse.LeftUp(0);
                        Mouse.LeftDown(0);
                        Mouse.LeftUp(0);
                        Mouse.MoveMouse(prevMousePosition);
                        Mouse.LeftDown(0);
                        Thread.Sleep(Settings.ClickDelay);
                        _clickedEntities[entity.Address] = _clickedEntities.GetValueOrDefault(entity.Address) + 1;
                    }
                    finally
                    {
                        if (Settings.BlockInputWhenClicking) Mouse.blockInput(false);
                    }
                }
            }
        }
        else
        {
            _clickedEntities.Remove(entity.Address);
        }
    }

    private int GetEntityClickedCount(Entity entity)
    {
        if (!_clickedEntities.TryGetValue(entity.Address, out var clickCount))
            _clickedEntities.Add(entity.Address, clickCount);

        if (clickCount >= 15) LogMessage(entity.Path + " clicked too often!", 3);
        return clickCount;
    }

    private List<string> LoadFile(string path)
    {
        try
        {
            var customPath = Path.Join(ConfigDirectory, path);
            if (File.Exists(customPath))
            {
                return File.ReadAllLines(customPath).ToList();
            }
            else
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(path);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"Cannot load {path}: {ex}");
        }

        return [];
    }

    private void ToggleDoorBlacklistItem(string name)
    {
        Toggle(_doorBlacklist, name, "doorBlacklist.txt", true);
    }

    private void ToggleSwitchBlacklistItem(string name)
    {
        Toggle(_switchBlacklist, name, "switchBlacklist.txt", true);
    }

    private void ToggleChestWhitelistItem(string name)
    {
        Toggle(_chestWhitelist, name, "chestWhitelist.txt", false);
    }

    private void Toggle(List<string> collection, string name, string path, bool isBlacklist)
    {
        if (collection.Remove(name))
        {
            LogMessage($"{name} will now be {(isBlacklist ? "opened" : "ignored")}", 5, Color.Red);
        }
        else
        {
            collection.Add(name);
            LogMessage($"{name} will now be {(isBlacklist ? "ignored" : "opened")}", 5, Color.Green);
        }

        File.WriteAllLines(Path.Join(ConfigDirectory, path), _chestWhitelist);
    }

    public override void AreaChange(AreaInstance area)
    {
        _clickedEntities.Clear();
    }
}
using System;
using System.Collections.Generic;
using Game.WaterSortPuzzle;

internal sealed class RulesCheck
{
    private readonly WaterSortPuzzleController m_controller = new WaterSortPuzzleController(); // 真实玩法规则。
    private readonly List<PourCommand> m_commands = new List<PourCommand>(); // 广播给无表现驱动器的命令。

    private RulesCheck()
    {
        m_controller.BeginPour += m_commands.Add;
        m_controller.DoPour += Receive;
    }

    private static int Main()
    {
        try
        {
            RulesCheck rules = new RulesCheck();
            rules.CheckSelectionAndUndo();
            rules.CheckParallelCompletion();
            rules.CheckComboUndo();
            rules.CheckLevels();
            rules.CheckInfinityReplacement();
            Console.WriteLine("倒水规则检查通过。");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private void CheckSelectionAndUndo()
    {
        Load(6);
        BottleData source = m_controller.Bottles[5];
        BottleData other = m_controller.Bottles[0];
        BottleData target = m_controller.Bottles[6];

        // 不兼容的满瓶接过选择；点回源瓶，再将可见顶层倒入空瓶。
        Require(m_controller.TrySelect(source) && m_controller.SelectedBottle == source, "应能选中隐藏瓶。");
        Require(m_controller.TrySelect(other) && m_controller.SelectedBottle == other, "不兼容目标应接过选择。");
        Require(m_controller.TrySelect(other) && m_controller.SelectedBottle == null, "再次点击应取消选择。");
        Require(m_controller.TrySelect(source) && m_controller.TrySelect(target), "空瓶应成为目标。");
        PourCommand waiting = m_commands[0];
        Require(waiting.ColorCount == 1 && target.LockCount == 1 && m_controller.SelectedBottle == null,
            "开始时应仅预占可见层并清空选择。");
        Require(m_controller.Reverse() && target.IsEmpty() && target.LockCount == 0, "等待接水时应只撤销预占。");
        m_controller.NotifyMovingFinished(waiting);
        Require(target.IsEmpty(), "撤销后的移动回调应失效。");

        // 已开始接水和已提交两种阶段都应恢复正确层数，提交还需恢复隐藏层。
        PourCommand receiving = Start(source, target);
        m_controller.NotifyMovingFinished(receiving);
        Require(target.Colors.Count == 1 && source.Colors.Count == 4, "接水阶段应只更新目标数据。");
        Require(m_controller.Reverse() && target.IsEmpty() && source.InvisibleCount == 3,
            "接水未提交时应只回退目标水层。");
        PourCommand finished = Start(source, target);
        Finish(finished);
        Require(source.InvisibleCount == 2 && source.Colors.Count == 3, "倒完后应揭晓下一层。");
        Require(m_controller.Reverse() && source.InvisibleCount == 3 && source.Colors.Count == 4 && target.IsEmpty(),
            "撤销已提交命令应恢复源瓶隐藏层及双方数据。");
    }

    private void CheckParallelCompletion()
    {
        Load(4);
        BottleData target = m_controller.Bottles[4];
        SetLayers(target, 1, 1);
        SetLayers(m_controller.Bottles[0], 1);
        SetLayers(m_controller.Bottles[1], 1);
        SetLayers(m_controller.Bottles[2], 1);
        PourCommand first = Start(m_controller.Bottles[0], target);
        PourCommand second = Start(m_controller.Bottles[1], target);
        Require(target.IsFull() && target.LockCount == 2, "两次接水应预占全部余量。");
        Require(!m_controller.TryPour(m_controller.Bottles[2], target), "额外命令不得超过预占容量。");
        Finish(second);
        m_controller.NotifyBottleComplete(target, second);
        Require(m_controller.Score == 0 && target.LockCount == 1, "仍有并行命令时不应提前完成。");
        Finish(first);
        m_controller.NotifyBottleComplete(target, first);
        m_controller.NotifyBottleComplete(target, first);
        Require(target.IsFinish() && m_controller.Score == WaterSortSettings.COMPLETIONSCORE,
            "乱序结束后目标瓶应仅计分一次。");
        Require(m_controller.Reverse() && target.Colors.Count == 3 && first.Source.IsEmpty()
            && second.Source.Colors.Count == 1 && m_controller.Score == 0,
            "撤销后开始的命令应扣除其完成奖励，保留先开始命令的水层。");

        PourCommand retry = Start(second.Source, target);
        Finish(retry);
        m_controller.NotifyBottleComplete(target, first);
        Require(m_controller.Score == 0, "同一瓶再次倒满后仍应拒绝上一次的完成回调。");
        m_controller.NotifyBottleComplete(target, retry);
        Require(m_controller.Score == WaterSortSettings.COMPLETIONSCORE, "重新完成应只接受新的命令令牌。");
    }

    private void CheckComboUndo()
    {
        Load(4);
        SetLayers(m_controller.Bottles[0], 1);
        SetLayers(m_controller.Bottles[1], 2);
        SetLayers(m_controller.Bottles[4], 1, 1, 1);
        SetLayers(m_controller.Bottles[5], 2, 2, 2);
        PourCommand first = Start(m_controller.Bottles[0], m_controller.Bottles[4]);
        Finish(first);
        m_controller.NotifyBottleComplete(first.Target, first);
        m_controller.Tick(2f);
        PourCommand second = Start(m_controller.Bottles[1], m_controller.Bottles[5]);
        Finish(second);
        m_controller.NotifyBottleComplete(second.Target, second);
        m_controller.Tick(1f);
        Require(m_controller.Combo == 2, "窗口内完成应延续连击。");
        Require(m_controller.Reverse() && m_controller.Combo == 1
            && m_controller.ComboRemainingTime == WaterSortSettings.COMBOWINDOW - 3f,
            "撤销应恢复前一次奖励剩余的窗口。");
        m_controller.Tick(WaterSortSettings.COMBOWINDOW);
        Require(m_controller.Combo == 0 && m_controller.ComboRemainingTime == 0f, "连击窗口应按耗时到期。");
    }

    private void CheckLevels()
    {
        Require(WaterSortLevelCatalog.Count == 14 && WaterSortLevelCatalog.Get(13).ReverseColor,
            "应包含十二个普通关、反色关和无尽关。");
        for (int levelID = 1; levelID < WaterSortLevelCatalog.Count; levelID++)
        {
            LevelDefinition level = WaterSortLevelCatalog.Get(levelID);
            Require(level.ID == levelID && level.PrerequisiteID == levelID - 1 && level.ReverseCount > 0,
                "普通和反色关应按编号依次解锁，并有固定撤销次数。");
            foreach (BottleData bottle in level.CreateBottles())
            {
                Require(bottle.IsEmpty() || bottle.Colors.Count == BottleData.CAPACITY,
                    "原始关卡中的瓶子应为满瓶或空瓶。");
            }
        }
        Require(WaterSortLevelCatalog.Get(14).PrerequisiteID == 12, "通关第十二关应开放无尽模式。");
        Load(14);
        int emptyCount = 0;
        int[] counts = new int[8];
        foreach (BottleData bottle in m_controller.Bottles)
        {
            if (bottle.IsEmpty())
                emptyCount++;
            Require(!bottle.IsFinish(), "无尽开局不能预先完成瓶子。");
            foreach (int color in bottle.Colors)
            {
                Require(color >= 1 && color <= 7, "无尽开局不能保留占位层。");
                counts[color]++;
            }
        }
        Require(m_controller.Bottles.Count == 33 && emptyCount == 3 && m_controller.ReverseRemaining == -1,
            "无尽应按模板保留三个空瓶并允许无限撤销。");
        for (int color = 1; color <= 7; color++)
            Require(counts[color] % BottleData.CAPACITY == 0, "开局颜色必须凑齐四层倍数。");
    }

    private void CheckInfinityReplacement()
    {
        Load(14);
        BottleData source = m_controller.Bottles[0];
        BottleData target = m_controller.Bottles[15];
        SetLayers(source, 1);
        SetLayers(target, 1, 1, 1);
        PourCommand command = Start(source, target);
        Finish(command);
        m_controller.NotifyBottleComplete(target, command);

        BottleData replacement = m_controller.Bottles[15];
        Require(replacement != target && replacement.Colors.Count == 4 && !replacement.IsFinish(),
            "无尽完成后应换入四层新瓶。");
        Require(m_controller.RemainingTime == WaterSortSettings.DURATION + WaterSortSettings.COMPLETIONTIME,
            "完成应奖励对应档位的剩余时间。");
        Require(m_controller.Reverse() && m_controller.Bottles[15] == target && target.Colors.Count == 3
            && m_controller.RemainingTime == WaterSortSettings.DURATION && m_controller.ReverseRemaining == -1,
            "无尽撤销应恢复旧瓶和加时，且不消耗撤销次数。");
        m_controller.Tick(WaterSortSettings.DURATION);
        Require(m_controller.IsStopped && m_controller.IsCompleted && !m_controller.CanReverse,
            "计时归零后应结算并冻结操作。");
    }

    private void Load(int levelID)
    {
        m_commands.Clear();
        m_controller.LoadLevel(levelID);
    }

    private PourCommand Start(BottleData source, BottleData target)
    {
        Require(m_controller.TryPour(source, target), "合法倒水应成功创建命令。");
        return m_commands[m_commands.Count - 1];
    }

    private void Finish(PourCommand command)
    {
        m_controller.NotifyMovingFinished(command);
        Require(command.Commit(), "接水后应成功提交源瓶数据。");
        m_controller.NotifyPourFinished(command);
    }

    private static void Receive(PourCommand command)
    {
        Require(command.Receive(), "仅活动命令可开始接水一次。");
    }

    private static void SetLayers(BottleData bottle, params int[] colors)
    {
        bottle.Colors.Clear();
        bottle.Colors.AddRange(colors);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

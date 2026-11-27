using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using AlloyFramework;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Game.WaterSortPuzzle
{
    /// <summary>管理有限关卡成绩、无尽最高分与本地进度存档。</summary>
    internal sealed class WaterSortProgressModel : ModelBase
    {
        private const string FILENAME = "WaterSortProgress.json"; // 本地进度存档文件名。
        private const int SAVEVERSION = 1; // 当前存档结构版本。

        private ProgressData m_progress; // 已成功加载或保存的进度快照。

        private ProgressData Progress => m_progress
            ?? throw new InvalidOperationException("关卡进度尚未加载。");

        internal async UniTask LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (m_progress != null)
                return;

            string path = Path.Combine(Application.persistentDataPath, FILENAME);
            string json;
            try
            {
                using (var reader = new StreamReader(path))
                {
                    json = await reader.ReadToEndAsync();
                }
            }
            catch (FileNotFoundException)
            {
                json = null;
            }
            catch (DirectoryNotFoundException)
            {
                json = null;
            }

            // 首次启动使用空存档；现有存档须通过验证后才替换内存数据。
            cancellationToken.ThrowIfCancellationRequested();
            m_progress ??= json == null
                ? new ProgressData { Version = SAVEVERSION, Records = new List<LevelRecord>() }
                : ReadProgress(json);
        }

        internal int GetBestScore(int levelID)
        {
            int index = FindRecordIndex(levelID);
            return index >= 0 ? Progress.Records[index].BestScore : 0;
        }

        internal bool IsCompleted(int levelID)
        {
            if (levelID < 1 || levelID > WaterSortLevelCatalog.Count)
                return false;

            int index = FindRecordIndex(levelID);
            if (index < 0)
                return false;

            LevelRecord record = Progress.Records[index];
            return WaterSortLevelCatalog.Get(levelID).IsInfinity
                ? record.BestScore > 0
                : record.HasCompletion || record.CompletedByGM;
        }

        internal bool IsUnlocked(int levelID)
        {
            if (levelID < 1 || levelID > WaterSortLevelCatalog.Count)
                return false;

            int prerequisiteID = WaterSortLevelCatalog.Get(levelID).PrerequisiteID;
            if (prerequisiteID == 0 || IsCompleted(prerequisiteID))
                return true;

            int index = FindRecordIndex(levelID);
            return index >= 0 && Progress.Records[index].UnlockedByGM;
        }

        internal bool TryGetLevelRecord(int levelID, out int bestMoveCount, out float fastestSeconds)
        {
            int index = FindRecordIndex(levelID);
            if (index >= 0 && Progress.Records[index].HasCompletion)
            {
                LevelRecord record = Progress.Records[index];
                bestMoveCount = record.BestMoveCount;
                fastestSeconds = record.FastestSeconds;
                return true;
            }

            bestMoveCount = -1;
            fastestSeconds = -1f;
            return false;
        }

        internal void RecordCompletion(int levelID, int moveCount, float elapsedSeconds)
        {
            if (!IsUnlocked(levelID) || WaterSortLevelCatalog.Get(levelID).IsInfinity
                || moveCount < 0 || !IsValidTime(elapsedSeconds))
                return;

            int index = FindRecordIndex(levelID);
            if (index >= 0 && Progress.Records[index].HasCompletion)
            {
                // 最少步数与最快用时分别比较，可以来自不同的通关回合。
                LevelRecord previousRecord = Progress.Records[index];
                if (moveCount >= previousRecord.BestMoveCount && elapsedSeconds >= previousRecord.FastestSeconds)
                    return;

                moveCount = Math.Min(moveCount, previousRecord.BestMoveCount);
                elapsedSeconds = Math.Min(elapsedSeconds, previousRecord.FastestSeconds);
            }

            SaveRecord(index, new LevelRecord
            {
                LevelID = levelID,
                HasCompletion = true,
                BestMoveCount = moveCount,
                FastestSeconds = elapsedSeconds
            });
        }

        internal void RecordBestScore(int levelID, int score)
        {
            if (score <= 0 || !IsUnlocked(levelID) || !WaterSortLevelCatalog.Get(levelID).IsInfinity)
                return;

            int index = FindRecordIndex(levelID);
            if (index >= 0 && score <= Progress.Records[index].BestScore)
                return;

            SaveRecord(index, new LevelRecord { LevelID = levelID, BestScore = score });
        }

        internal void CompleteNormalLevelsThrough(int targetNumber)
        {
            // 选关页按目录顺序显示普通关；跳过无尽关和独立挑战的内部 ID。
            var records = new List<LevelRecord>(Progress.Records);
            int normalNumber = 0;
            bool changed = false;
            for (int id = 1; id <= WaterSortLevelCatalog.Count; id++)
            {
                if (WaterSortLevelCatalog.Get(id).IsChallenge)
                    continue;

                normalNumber++;
                if (!IsCompleted(id))
                {
                    int index = FindRecordIndex(id);
                    LevelRecord record = index < 0 ? new LevelRecord { LevelID = id } : records[index];
                    record.CompletedByGM = true;
                    if (index < 0)
                        records.Add(record);
                    else
                        records[index] = record;
                    changed = true;
                }

                if (normalNumber == targetNumber)
                {
                    if (changed)
                        SaveRecords(records);
                    return;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(targetNumber), $"请输入 1 到 {normalNumber} 的普通关卡序号。");
        }

        internal void UnlockLevelsForGM(int? endlessNumber = null)
        {
            // 指定序号时只解锁无尽前 N 关；不指定时解锁整个目录，包含独立挑战。
            var records = new List<LevelRecord>(Progress.Records);
            bool changed = false;
            int number = 0;
            for (int id = 1; id <= WaterSortLevelCatalog.Count; id++)
            {
                if (endlessNumber.HasValue && !WaterSortLevelCatalog.Get(id).IsInfinity)
                    continue;

                number++;
                if (!IsUnlocked(id))
                {
                    int index = FindRecordIndex(id);
                    LevelRecord record = index < 0 ? new LevelRecord { LevelID = id } : records[index];
                    record.UnlockedByGM = true;
                    if (index < 0)
                        records.Add(record);
                    else
                        records[index] = record;
                    changed = true;
                }

                if (number == endlessNumber)
                    break;
            }

            // 无效输入丢弃暂存修改；成功时只写一次，不生成成绩或推进通关状态。
            if (endlessNumber.HasValue && number != endlessNumber)
                throw new ArgumentOutOfRangeException(nameof(endlessNumber), $"请输入 1 到 {number} 的无尽关卡序号。");
            if (changed)
                SaveRecords(records);
        }

        internal void DeleteSave()
        {
            // 先删除临时文件；正式文件删除成功后再发布空快照。
            string directory = Application.persistentDataPath;
            try
            {
                string path = Path.Combine(directory, FILENAME);
                File.Delete(path + ".tmp");
                File.Delete(path);
            }
            catch (DirectoryNotFoundException)
            {
                // 首次启动尚未创建存档目录时，磁盘已处于空档状态。
            }

            m_progress = new ProgressData { Version = SAVEVERSION, Records = new List<LevelRecord>() };
        }

        private static ProgressData ReadProgress(string json)
        {
            ProgressData progress;
            try
            {
                progress = JsonUtility.FromJson<ProgressData>(json);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("无法解析关卡进度存档。", exception);
            }

            // 读取时拒绝版本不兼容、字段无效和关卡记录重复的存档。
            if (progress == null || progress.Version != SAVEVERSION || progress.Records == null)
                throw new InvalidDataException("关卡进度存档的结构或版本无效。");

            var levelIDs = new HashSet<int>();
            foreach (LevelRecord record in progress.Records)
            {
                bool validResult = record.HasCompletion
                    ? record.BestScore == 0 && record.BestMoveCount >= 0 && IsValidTime(record.FastestSeconds)
                    : (record.BestScore > 0 || ((record.CompletedByGM || record.UnlockedByGM) && record.BestScore == 0))
                        && record.BestMoveCount == 0 && record.FastestSeconds == 0f;
                if (record.LevelID < 1 || !validResult || !levelIDs.Add(record.LevelID))
                    throw new InvalidDataException($"关卡进度存档包含无效记录，关卡编号 {record.LevelID}。");
            }

            return progress;
        }

        private static bool IsValidTime(float seconds)
        {
            return seconds >= 0f && !float.IsInfinity(seconds);
        }

        private int FindRecordIndex(int levelID)
        {
            List<LevelRecord> records = Progress.Records;
            for (int index = 0; index < records.Count; index++)
            {
                if (records[index].LevelID == levelID)
                    return index;
            }

            return -1;
        }

        private void SaveRecord(int index, LevelRecord record)
        {
            // 在写盘前构建新快照，保存失败时仍以旧存档为准。
            var records = new List<LevelRecord>(Progress.Records);
            if (index < 0)
                records.Add(record);
            else
            {
                record.UnlockedByGM = records[index].UnlockedByGM;
                records[index] = record;
            }

            SaveRecords(records);
        }

        private void SaveRecords(List<LevelRecord> records)
        {
            var pendingProgress = new ProgressData { Version = SAVEVERSION, Records = records };
            string directory = Application.persistentDataPath;
            string path = Path.Combine(directory, FILENAME);
            string temporaryPath = path + ".tmp";

            // 先写入同一目录，再替换正式文件，避免半写的内容被当成存档读取。
            Directory.CreateDirectory(directory);
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(pendingProgress));
            if (File.Exists(path))
                File.Replace(temporaryPath, path, null);
            else
                File.Move(temporaryPath, path);

            m_progress = pendingProgress;
        }

        [Serializable]
        private sealed class ProgressData
        {
            /// <summary>存档结构版本。</summary>
            public int Version;

            /// <summary>以关卡编号区分的已保存成绩。</summary>
            public List<LevelRecord> Records;
        }

        [Serializable]
        private struct LevelRecord
        {
            /// <summary>关卡目录中的唯一编号。</summary>
            public int LevelID;

            /// <summary>该记录是否保存了有限关卡的通关成绩。</summary>
            public bool HasCompletion;

            /// <summary>由 GM 标记通关，个人成绩在玩家实际通关时记录。</summary>
            public bool CompletedByGM;

            /// <summary>GM 直接授予进入权限，独立于通关和得分。</summary>
            public bool UnlockedByGM;

            /// <summary>有限关卡最少步数。</summary>
            public int BestMoveCount;

            /// <summary>有限关卡最快用时，单位为秒。</summary>
            public float FastestSeconds;

            /// <summary>无尽关卡最高分。</summary>
            public int BestScore;
        }
    }
}

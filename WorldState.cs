// ============================================================================
// WorldState.cs — 全局世界状态，所有 Agent 共享的"黑板"
// ============================================================================
using UnityEngine;
using System;
using System.Collections.Generic;

namespace AICity.Core
{
    /// <summary>
    /// 世界状态 — 存储所有 Agent 共享的全局信息
    /// 类似于"黑板"模式，Agent 读写共享数据
    /// </summary>
    public class WorldState : MonoBehaviour
    {
        public static WorldState Instance { get; private set; }

        // ========== 城市基础数据 ==========
        [Header("城市统计")]
        public int TotalPopulation;
        public int TotalBuildings;
        public float CityHappiness;      // 0-100
        public float CitySafety;         // 0-100
        public float CityPollution;      // 0-100

        // ========== 时间系统 ==========
        [Header("时间")]
        public float GameTime;           // 游戏内时间（小时）
        public float TimeScale = 1f;     // 时间缩放
        public DayOfWeek CurrentDay;
        public int CurrentWeek;
        public int CurrentMonth;

        // ========== 经济数据 ==========
        [Header("经济")]
        public float CityBudget;
        public float TaxRate = 0.15f;
        public float UnemploymentRate;
        public float AverageIncome;
        public float InflationRate;

        // ========== 基础设施 ==========
        [Header("基础设施")]
        public float RoadCapacity;
        public float PublicTransportCoverage; // 0-1
        public float HospitalCapacity;
        public float SchoolCapacity;
        public float PowerCapacity;
        public float WaterCapacity;

        // ========== 建筑注册表 ==========
        private Dictionary<Vector2Int, BuildingData> buildings
            = new Dictionary<Vector2Int, BuildingData>();

        // ========== 区域数据 ==========
        private Dictionary<string, ZoneData> zones
            = new Dictionary<string, ZoneData>();

        // ========== 事件 ==========

        public event Action<string, object> OnWorldEvent;
        public event Action<BuildingData> OnBuildingPlaced;
        public event Action<BuildingData> OnBuildingRemoved;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeWorld();
        }

        private void Update()
        {
            UpdateTime();
        }

        private void InitializeWorld()
        {
            GameTime = 8f; // 早上 8 点开始
            CurrentDay = DayOfWeek.Monday;
            CurrentWeek = 1;
            CurrentMonth = 1;
            CityBudget = 1000000f;
            TotalPopulation = 100;
            CityHappiness = 60f;
            CitySafety = 70f;
            CityPollution = 20f;
        }

        private void UpdateTime()
        {
            // 1 真实秒 = 1 游戏分钟（可配置）
            GameTime += Time.deltaTime * TimeScale / 60f;

            if (GameTime >= 24f)
            {
                GameTime -= 24f;
                CurrentDay = (DayOfWeek)(((int)CurrentDay + 1) % 7);
                if (CurrentDay == DayOfWeek.Monday)
                {
                    CurrentWeek++;
                    if (CurrentWeek > 4)
                    {
                        CurrentWeek = 1;
                        CurrentMonth++;
                    }
                }
            }
        }

        // ========== 建筑管理 ==========

        public void PlaceBuilding(BuildingData building)
        {
            buildings[building.GridPosition] = building;
            TotalBuildings++;
            OnBuildingPlaced?.Invoke(building);
        }

        public void RemoveBuilding(Vector2Int gridPos)
        {
            if (buildings.TryGetValue(gridPos, out var building))
            {
                buildings.Remove(gridPos);
                TotalBuildings--;
                OnBuildingRemoved?.Invoke(building);
            }
        }

        public BuildingData GetBuilding(Vector2Int gridPos)
        {
            buildings.TryGetValue(gridPos, out var building);
            return building;
        }

        public List<BuildingData> GetBuildingsByType(BuildingType type)
        {
            var result = new List<BuildingData>();
            foreach (var b in buildings.Values)
            {
                if (b.Type == type)
                    result.Add(b);
            }
            return result;
        }

        // ========== 区域管理 ==========

        public void RegisterZone(ZoneData zone)
        {
            zones[zone.ZoneId] = zone;
        }

        public ZoneData GetZone(string zoneId)
        {
            zones.TryGetValue(zoneId, out var zone);
            return zone;
        }

        // ========== 经济操作 ==========

        public bool SpendBudget(float amount, string reason)
        {
            if (CityBudget >= amount)
            {
                CityBudget -= amount;
                OnWorldEvent?.Invoke("budget_spent", new { Amount = amount, Reason = reason });
                return true;
            }
            return false;
        }

        public void AddRevenue(float amount, string source)
        {
            CityBudget += amount;
            OnWorldEvent?.Invoke("budget_received", new { Amount = amount, Source = source });
        }

        // ========== 全局查询 ==========

        public bool IsNightTime()
        {
            return GameTime < 6f || GameTime > 22f;
        }

        public bool IsRushHour()
        {
            return (GameTime >= 7f && GameTime <= 9f) || (GameTime >= 17f && GameTime <= 19f);
        }

        public float GetTimeNormalized()
        {
            return GameTime / 24f;
        }
    }

    // ========== 数据结构 ==========

    [System.Serializable]
    public class BuildingData
    {
        public string BuildingId;
        public string BuildingName;
        public BuildingType Type;
        public Vector2Int GridPosition;
        public Vector3 WorldPosition;
        public float ConstructionProgress; // 0-1
        public bool IsOperational;
        public int Capacity;
        public int CurrentOccupancy;
        public float MaintenanceCost;
        public float Condition; // 0-1, 建筑状况
    }

    public enum BuildingType
    {
        Residential,
        Commercial,
        Industrial,
        Hospital,
        School,
        PoliceStation,
        FireStation,
        PowerPlant,
        WaterPlant,
        Park,
        Road,
        Highway,
        BusStation,
        Subway
    }

    [System.Serializable]
    public class ZoneData
    {
        public string ZoneId;
        public string ZoneName;
        public ZoneType Type;
        public List<Vector2Int> Cells;
        public float Density; // 0-1
        public float Desirability; // 0-1
    }

    public enum ZoneType
    {
        Residential,
        Commercial,
        Industrial,
        Mixed
    }
}

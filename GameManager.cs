// ============================================================================
// GameManager.cs — 游戏主控制器，初始化和协调所有系统
// ============================================================================
using UnityEngine;
using System.Collections;
using AICity.Core;
using AICity.Agents;

namespace AICity
{
    /// <summary>
    /// 游戏管理器 — 负责初始化所有系统、管理游戏流程
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("预制体")]
        [SerializeField] private GameObject citizenPrefab;
        [SerializeField] private GameObject trafficAgentPrefab;
        [SerializeField] private GameObject economyAgentPrefab;
        [SerializeField] private GameObject policyAgentPrefab;

        [Header("城市配置")]
        [SerializeField] private int initialCitizenCount = 100;
        [SerializeField] private int trafficAgentCount = 4;
        [SerializeField] private int economyAgentCount = 5;
        [SerializeField] private Vector2 citySize = new Vector2(500f, 500f);

        [Header("系统管理器")]
        [SerializeField] private GameObject eventBusPrefab;
        [SerializeField] private GameObject agentManagerPrefab;
        [SerializeField] private GameObject worldStatePrefab;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            StartCoroutine(InitializeCity());
        }

        /// <summary>
        /// 城市初始化协程
        /// </summary>
        private IEnumerator InitializeCity()
        {
            Debug.Log("[GameManager] 开始初始化城市...");

            // 1. 创建核心系统
            yield return CreateCoreSystems();
            yield return null; // 等待一帧确保初始化完成

            // 2. 创建政策 Agent（1 个）
            yield return CreatePolicyAgent();

            // 3. 创建经济 Agent（按行业）
            yield return CreateEconomyAgents();

            // 4. 创建交通 Agent（按区域）
            yield return CreateTrafficAgents();

            // 5. 生成市民
            yield return SpawnCitizens();

            // 6. 生成初始建筑
            yield return GenerateInitialBuildings();

            Debug.Log("[GameManager] 城市初始化完成！");
            Debug.Log($"[GameManager] 市民: {initialCitizenCount}, Agent 总数: {AgentManager.Instance.AgentCount}");

            // 广播城市就绪事件
            EventBus.Instance.Publish("system", "city_ready", new
            {
                population = initialCitizenCount,
                agents = AgentManager.Instance.AgentCount
            });
        }

        private IEnumerator CreateCoreSystems()
        {
            // EventBus
            if (EventBus.Instance == null)
            {
                var obj = Instantiate(eventBusPrefab ?? new GameObject("EventBus"));
                obj.name = "EventBus";
                obj.AddComponent<EventBus>();
            }

            // AgentManager
            if (AgentManager.Instance == null)
            {
                var obj = Instantiate(agentManagerPrefab ?? new GameObject("AgentManager"));
                obj.name = "AgentManager";
                obj.AddComponent<AgentManager>();
            }

            // WorldState
            if (WorldState.Instance == null)
            {
                var obj = Instantiate(worldStatePrefab ?? new GameObject("WorldState"));
                obj.name = "WorldState";
                obj.AddComponent<WorldState>();
            }

            yield return null;
        }

        private IEnumerator CreatePolicyAgent()
        {
            if (policyAgentPrefab == null)
            {
                // 如果没有预制体，动态创建
                var obj = new GameObject("PolicyAgent");
                obj.AddComponent<PolicyAgent>();
                Debug.Log("[GameManager] 创建政策 Agent（动态）");
            }
            else
            {
                var obj = Instantiate(policyAgentPrefab, Vector3.zero, Quaternion.identity);
                obj.name = "PolicyAgent";
                Debug.Log("[GameManager] 创建政策 Agent（预制体）");
            }

            yield return null;
        }

        private IEnumerator CreateEconomyAgents()
        {
            var industries = new[]
            {
                IndustryType.Food,
                IndustryType.Housing,
                IndustryType.Retail,
                IndustryType.Healthcare,
                IndustryType.Technology
            };

            for (int i = 0; i < economyAgentCount && i < industries.Length; i++)
            {
                GameObject obj;
                if (economyAgentPrefab != null)
                {
                    obj = Instantiate(economyAgentPrefab, GetRandomPosition(), Quaternion.identity);
                }
                else
                {
                    obj = new GameObject($"EconomyAgent_{industries[i]}");
                    obj.transform.position = GetRandomPosition();
                    obj.AddComponent<EconomyAgent>();
                }
                obj.name = $"EconomyAgent_{industries[i]}";

                // 设置行业（通过反射或公开方法）
                var agent = obj.GetComponent<EconomyAgent>();
                if (agent != null)
                {
                    // 这里需要 EconomyAgent 暴露初始化方法
                    // agent.Initialize(industries[i]);
                }

                Debug.Log($"[GameManager] 创建经济 Agent: {industries[i]}");

                if (i % 2 == 0) yield return null; // 每 2 个让一帧
            }
        }

        private IEnumerator CreateTrafficAgents()
        {
            // 将城市划分为网格，每个网格一个交通 Agent
            int gridSize = Mathf.CeilToInt(Mathf.Sqrt(trafficAgentCount));

            for (int i = 0; i < trafficAgentCount; i++)
            {
                int row = i / gridSize;
                int col = i % gridSize;

                float x = (col - gridSize / 2f) * (citySize.x / gridSize);
                float z = (row - gridSize / 2f) * (citySize.y / gridSize);
                Vector3 position = new Vector3(x, 0, z);

                GameObject obj;
                if (trafficAgentPrefab != null)
                {
                    obj = Instantiate(trafficAgentPrefab, position, Quaternion.identity);
                }
                else
                {
                    obj = new GameObject($"TrafficAgent_Zone{i}");
                    obj.transform.position = position;
                    obj.AddComponent<TrafficAgent>();
                }
                obj.name = $"TrafficAgent_Zone{i}";

                Debug.Log($"[GameManager] 创建交通 Agent: Zone{i} @ {position}");

                if (i % 2 == 0) yield return null;
            }
        }

        private IEnumerator SpawnCitizens()
        {
            Debug.Log($"[GameManager] 生成 {initialCitizenCount} 名市民...");

            for (int i = 0; i < initialCitizenCount; i++)
            {
                Vector3 position = GetRandomPosition();

                GameObject obj;
                if (citizenPrefab != null)
                {
                    obj = Instantiate(citizenPrefab, position, Quaternion.identity);
                }
                else
                {
                    obj = new GameObject($"Citizen_{i}");
                    obj.transform.position = position;
                    obj.AddComponent<CitizenAgent>();

                    // 添加 NavMeshAgent（如果需要导航）
                    var navAgent = obj.AddComponent<UnityEngine.AI.NavMeshAgent>();
                    navAgent.speed = 3.5f;
                    navAgent.radius = 0.3f;
                }
                obj.name = $"Citizen_{i}";
                obj.transform.parent = transform; // 组织层级

                // 每 10 个市民让一帧，避免卡顿
                if (i % 10 == 0)
                    yield return null;
            }

            WorldState.Instance.TotalPopulation = initialCitizenCount;
        }

        private IEnumerator GenerateInitialBuildings()
        {
            Debug.Log("[GameManager] 生成初始建筑...");

            // 生成住宅
            for (int i = 0; i < 20; i++)
            {
                CreateBuilding(BuildingType.Residential, $"住宅_{i}", GetRandomPosition());
            }

            // 生成商业
            for (int i = 0; i < 10; i++)
            {
                CreateBuilding(BuildingType.Commercial, $"商业_{i}", GetRandomPosition());
            }

            // 生成基础设施
            CreateBuilding(BuildingType.Hospital, "市中心医院", Vector3.zero + Vector3.right * 50);
            CreateBuilding(BuildingType.School, "第一中学", Vector3.zero + Vector3.left * 50);
            CreateBuilding(BuildingType.PoliceStation, "警察局", Vector3.zero + Vector3.forward * 50);
            CreateBuilding(BuildingType.FireStation, "消防站", Vector3.zero + Vector3.back * 50);

            // 生成公园
            for (int i = 0; i < 5; i++)
            {
                CreateBuilding(BuildingType.Park, $"公园_{i}", GetRandomPosition());
            }

            yield return null;
        }

        // ========== 辅助方法 ==========

        private void CreateBuilding(BuildingType type, string name, Vector3 position)
        {
            var building = new BuildingData
            {
                BuildingId = System.Guid.NewGuid().ToString("N").Substring(0, 8),
                BuildingName = name,
                Type = type,
                GridPosition = new Vector2Int(
                    Mathf.RoundToInt(position.x / 2f),
                    Mathf.RoundToInt(position.z / 2f)),
                WorldPosition = position,
                ConstructionProgress = 1f,
                IsOperational = true,
                Capacity = GetDefaultCapacity(type),
                CurrentOccupancy = 0,
                MaintenanceCost = 100f,
                Condition = 1f
            };

            WorldState.Instance.PlaceBuilding(building);

            // 创建可视化的建筑对象
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name;
            obj.transform.position = position + Vector3.up * 2f;
            obj.transform.localScale = GetBuildingScale(type);

            // 设置颜色
            var renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = GetBuildingColor(type);
            }
        }

        private int GetDefaultCapacity(BuildingType type)
        {
            return type switch
            {
                BuildingType.Residential => 50,
                BuildingType.Commercial => 100,
                BuildingType.Hospital => 200,
                BuildingType.School => 500,
                BuildingType.PoliceStation => 30,
                BuildingType.FireStation => 20,
                BuildingType.Park => 500,
                _ => 50
            };
        }

        private Vector3 GetBuildingScale(BuildingType type)
        {
            return type switch
            {
                BuildingType.Residential => new Vector3(8, 10, 8),
                BuildingType.Commercial => new Vector3(12, 6, 12),
                BuildingType.Industrial => new Vector3(15, 8, 15),
                BuildingType.Hospital => new Vector3(20, 12, 20),
                BuildingType.School => new Vector3(18, 8, 18),
                BuildingType.Park => new Vector3(25, 1, 25),
                _ => new Vector3(10, 8, 10)
            };
        }

        private Color GetBuildingColor(BuildingType type)
        {
            return type switch
            {
                BuildingType.Residential => new Color(0.8f, 0.7f, 0.6f),  // 米色
                BuildingType.Commercial => new Color(0.3f, 0.5f, 0.8f),   // 蓝色
                BuildingType.Industrial => new Color(0.5f, 0.5f, 0.5f),   // 灰色
                BuildingType.Hospital => new Color(1f, 0.9f, 0.9f),       // 白粉色
                BuildingType.School => new Color(0.9f, 0.8f, 0.3f),       // 黄色
                BuildingType.PoliceStation => new Color(0.2f, 0.3f, 0.6f), // 深蓝
                BuildingType.FireStation => new Color(0.9f, 0.2f, 0.2f),  // 红色
                BuildingType.Park => new Color(0.3f, 0.7f, 0.3f),         // 绿色
                BuildingType.PowerPlant => new Color(0.8f, 0.8f, 0.2f),   // 黄色
                _ => Color.white
            };
        }

        private Vector3 GetRandomPosition()
        {
            return new Vector3(
                Random.Range(-citySize.x / 2f, citySize.x / 2f),
                0f,
                Random.Range(-citySize.y / 2f, citySize.y / 2f)
            );
        }
    }
}

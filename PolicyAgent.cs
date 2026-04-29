// ============================================================================
// PolicyAgent.cs — 政策 Agent：自然语言输入 → 城市政策执行
// ============================================================================
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using AICity.Core;

namespace AICity.Agents
{
    /// <summary>
    /// 政策 Agent — 将玩家的自然语言指令转化为可执行的城市政策
    /// 核心能力：意图识别、政策分解、影响评估、多 Agent 协调执行
    /// </summary>
    public class PolicyAgent : AgentBase
    {
        [Header("政策系统")]
        [SerializeField] private List<ActivePolicy> activePolicies = new List<ActivePolicy>();
        [SerializeField] private float approvalRate = 60f; // 市民支持率

        // 政策知识库
        private Dictionary<string, PolicyTemplate> policyTemplates = new Dictionary<string, PolicyTemplate>();

        // 影响评估模型
        private ImpactModel impactModel;

        // 待执行的政策队列
        private Queue<PolicyData> pendingPolicies = new Queue<PolicyData>();

        protected override void Awake()
        {
            base.Awake();
            impactModel = new ImpactModel();
            InitializePolicyTemplates();
        }

        protected override void InitializeMemory()
        {
            Remember("approval_rate", approvalRate, 0.9f);
            Remember("active_policy_count", 0, 0.7f);
        }

        protected override void SubscribeEvents()
        {
            eventBus.Subscribe("natural_language_command", OnNaturalLanguageCommand);
            eventBus.Subscribe("policy_feedback", OnPolicyFeedback);
            eventBus.Subscribe("citizen_complaint", OnCitizenComplaint);
            eventBus.Subscribe("suggest_interest_rate_hike", OnInterestRateSuggestion);
            eventBus.Subscribe("infrastructure_investment", OnInfrastructureRequest);
        }

        protected override void UnsubscribeEvents()
        {
            eventBus.Unsubscribe("natural_language_command", OnNaturalLanguageCommand);
            eventBus.Unsubscribe("policy_feedback", OnPolicyFeedback);
            eventBus.Unsubscribe("citizen_complaint", OnCitizenComplaint);
            eventBus.Unsubscribe("suggest_interest_rate_hike", OnInterestRateSuggestion);
            eventBus.Unsubscribe("infrastructure_investment", OnInfrastructureRequest);
        }

        // ========== 核心循环 ==========

        protected override void Perception()
        {
            // 1. 监控已实施政策的效果
            MonitorActivePolicies();

            // 2. 评估市民满意度
            EvaluateCitizenSatisfaction();

            // 3. 收集各行业 Agent 的报告
            CollectAgentReports();
        }

        protected override void Think()
        {
            // 1. 评估当前政策效果
            EvaluatePolicyEffectiveness();

            // 2. 识别需要新政策的问题
            IdentifyProblems();

            // 3. 检查是否有待执行的政策
            if (pendingPolicies.Count > 0)
            {
                currentState = AgentState.Planning;
            }
        }

        protected override void Decide()
        {
            if (currentState != AgentState.Planning)
                return;

            if (pendingPolicies.Count > 0)
            {
                var policy = pendingPolicies.Dequeue();

                // 影响评估
                var impact = impactModel.Evaluate(policy, worldState);

                if (impact.Feasibility > 0.3f) // 可行性阈值
                {
                    currentTask = new AgentTask
                    {
                        TaskType = "implement_policy",
                        Description = $"实施政策: {policy.Name}",
                        Priority = policy.Urgency,
                        Parameters = new Dictionary<string, object>
                        {
                            { "policy", policy },
                            { "impact", impact }
                        }
                    };
                }
                else
                {
                    // 政策不可行，通知玩家
                    EmitEvent("policy_rejected", new
                    {
                        policy = policy.Name,
                        reason = impact.RejectionReason,
                        suggestion = impact.AlternativeSuggestion
                    });
                }
            }

            currentState = AgentState.Executing;
        }

        protected override void Act()
        {
            if (currentTask == null) return;

            if (currentTask.TaskType == "implement_policy")
            {
                var policy = (PolicyData)currentTask.Parameters["policy"];
                var impact = (ImpactAssessment)currentTask.Parameters["impact"];
                ImplementPolicy(policy, impact);
            }
        }

        // ========== 自然语言处理 ==========

        /// <summary>
        /// 解析自然语言指令，转化为政策数据
        /// </summary>
        public PolicyData ParseNaturalLanguage(string input)
        {
            // 关键词匹配和意图识别
            var tokens = Tokenize(input.ToLower());
            var intent = IdentifyIntent(tokens);
            var entities = ExtractEntities(tokens);

            // 构建政策数据
            var policy = new PolicyData
            {
                PolicyId = Guid.NewGuid().ToString("N").Substring(0, 8),
                RawInput = input,
                Timestamp = Time.time
            };

            switch (intent)
            {
                case PolicyIntent.TaxChange:
                    policy = BuildTaxPolicy(entities, input);
                    break;
                case PolicyIntent.ZoneChange:
                    policy = BuildZonePolicy(entities, input);
                    break;
                case PolicyIntent.Infrastructure:
                    policy = BuildInfrastructurePolicy(entities, input);
                    break;
                case PolicyIntent.PublicService:
                    policy = BuildPublicServicePolicy(entities, input);
                    break;
                case PolicyIntent.Emergency:
                    policy = BuildEmergencyPolicy(entities, input);
                    break;
                case PolicyIntent.Environmental:
                    policy = BuildEnvironmentalPolicy(entities, input);
                    break;
                default:
                    policy.Type = PolicyType.Unknown;
                    policy.RejectionReason = "无法识别指令意图，请重新描述";
                    break;
            }

            return policy;
        }

        private string[] Tokenize(string input)
        {
            // 简单分词（实际项目中应使用 NLP 库）
            char[] separators = { ' ', ',', '.', '!', '?', '，', '。', '！', '？', '的', '了', '在', '是' };
            return input.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        }

        private PolicyIntent IdentifyIntent(string[] tokens)
        {
            // 基于关键词的意图识别
            var keywordMap = new Dictionary<PolicyIntent, string[]>
            {
                { PolicyIntent.TaxChange, new[] { "税", "税收", "税率", "减税", "加税", "tax", "降低", "提高" } },
                { PolicyIntent.ZoneChange, new[] { "区域", "分区", "规划", "zone", "住宅", "商业", "工业" } },
                { PolicyIntent.Infrastructure, new[] { "道路", "桥", "地铁", "公交", "基建", "基础设施", "road", "bridge" } },
                { PolicyIntent.PublicService, new[] { "医院", "学校", "消防", "警察", "服务", "hospital", "school" } },
                { PolicyIntent.Emergency, new[] { "紧急", "疏散", "灾难", "火灾", "洪水", "emergency", "disaster" } },
                { PolicyIntent.Environmental, new[] { "环境", "污染", "绿化", "公园", "垃圾", "environment", "pollution" } }
            };

            foreach (var kvp in keywordMap)
            {
                foreach (var keyword in kvp.Value)
                {
                    if (tokens.Any(t => t.Contains(keyword)))
                        return kvp.Key;
                }
            }

            return PolicyIntent.Unknown;
        }

        private Dictionary<string, string> ExtractEntities(string[] tokens)
        {
            var entities = new Dictionary<string, string>();

            // 提取数字
            foreach (var token in tokens)
            {
                if (float.TryParse(token.Replace("%", ""), out float num))
                {
                    entities["number"] = num.ToString();
                    if (token.Contains("%"))
                        entities["is_percentage"] = "true";
                }
            }

            // 提取方向/位置
            var directions = new[] { "东", "南", "西", "北", "中心", "郊区" };
            foreach (var dir in directions)
            {
                if (tokens.Any(t => t.Contains(dir)))
                {
                    entities["direction"] = dir;
                    break;
                }
            }

            return entities;
        }

        // ========== 政策构建 ==========

        private PolicyData BuildTaxPolicy(Dictionary<string, string> entities, string rawInput)
        {
            var policy = new PolicyData
            {
                Type = PolicyType.TaxChange,
                Name = "税收调整",
                Description = rawInput,
                Urgency = 0.5f
            };

            if (entities.TryGetValue("number", out string numStr))
            {
                float rate = float.Parse(numStr);
                if (entities.ContainsKey("is_percentage"))
                    policy.Parameters["tax_rate"] = rate / 100f;
                else
                    policy.Parameters["tax_rate"] = rate;
            }

            // 判断是增税还是减税
            bool isIncrease = rawInput.Contains("提高") || rawInput.Contains("增加") || rawInput.Contains("加");
            policy.Parameters["is_increase"] = isIncrease;

            return policy;
        }

        private PolicyData BuildZonePolicy(Dictionary<string, string> entities, string rawInput)
        {
            var policy = new PolicyData
            {
                Type = PolicyType.ZoneChange,
                Name = "区域规划调整",
                Description = rawInput,
                Urgency = 0.4f
            };

            // 确定区域类型
            if (rawInput.Contains("住宅") || rawInput.Contains("居住"))
                policy.Parameters["zone_type"] = ZoneType.Residential;
            else if (rawInput.Contains("商业"))
                policy.Parameters["zone_type"] = ZoneType.Commercial;
            else if (rawInput.Contains("工业"))
                policy.Parameters["zone_type"] = ZoneType.Industrial;
            else
                policy.Parameters["zone_type"] = ZoneType.Mixed;

            // 确定方向
            if (entities.TryGetValue("direction", out string dir))
                policy.Parameters["direction"] = dir;

            return policy;
        }

        private PolicyData BuildInfrastructurePolicy(Dictionary<string, string> entities, string rawInput)
        {
            var policy = new PolicyData
            {
                Type = PolicyType.Infrastructure,
                Name = "基础设施建设",
                Description = rawInput,
                Urgency = 0.6f
            };

            // 确定建设类型
            if (rawInput.Contains("地铁"))
                policy.Parameters["build_type"] = BuildingType.Subway;
            else if (rawInput.Contains("公交"))
                policy.Parameters["build_type"] = BuildingType.BusStation;
            else if (rawInput.Contains("高速"))
                policy.Parameters["build_type"] = BuildingType.Highway;
            else
                policy.Parameters["build_type"] = BuildingType.Road;

            if (entities.TryGetValue("direction", out string dir))
                policy.Parameters["direction"] = dir;

            return policy;
        }

        private PolicyData BuildPublicServicePolicy(Dictionary<string, string> entities, string rawInput)
        {
            var policy = new PolicyData
            {
                Type = PolicyType.PublicService,
                Name = "公共服务调整",
                Description = rawInput,
                Urgency = 0.7f
            };

            if (rawInput.Contains("医院"))
                policy.Parameters["service_type"] = BuildingType.Hospital;
            else if (rawInput.Contains("学校"))
                policy.Parameters["service_type"] = BuildingType.School;
            else if (rawInput.Contains("消防"))
                policy.Parameters["service_type"] = BuildingType.FireStation;
            else if (rawInput.Contains("警察"))
                policy.Parameters["service_type"] = BuildingType.PoliceStation;

            return policy;
        }

        private PolicyData BuildEmergencyPolicy(Dictionary<string, string> entities, string rawInput)
        {
            var policy = new PolicyData
            {
                Type = PolicyType.Emergency,
                Name = "紧急响应",
                Description = rawInput,
                Urgency = 1f
            };

            if (rawInput.Contains("疏散"))
                policy.Parameters["emergency_type"] = "evacuation";
            else if (rawInput.Contains("火灾"))
                policy.Parameters["emergency_type"] = "fire";
            else if (rawInput.Contains("洪水"))
                policy.Parameters["emergency_type"] = "flood";

            return policy;
        }

        private PolicyData BuildEnvironmentalPolicy(Dictionary<string, string> entities, string rawInput)
        {
            var policy = new PolicyData
            {
                Type = PolicyType.Environmental,
                Name = "环境政策",
                Description = rawInput,
                Urgency = 0.5f
            };

            if (rawInput.Contains("绿化") || rawInput.Contains("公园"))
                policy.Parameters["action"] = "green";
            else if (rawInput.Contains("污染"))
                policy.Parameters["action"] = "reduce_pollution";
            else if (rawInput.Contains("垃圾"))
                policy.Parameters["action"] = "waste_management";

            return policy;
        }

        // ========== 政策执行 ==========

        private void ImplementPolicy(PolicyData policy, ImpactAssessment impact)
        {
            Debug.Log($"[PolicyAgent] 实施政策: {policy.Name} ({policy.Type})");

            switch (policy.Type)
            {
                case PolicyType.TaxChange:
                    ImplementTaxChange(policy);
                    break;
                case PolicyType.ZoneChange:
                    ImplementZoneChange(policy);
                    break;
                case PolicyType.Infrastructure:
                    ImplementInfrastructure(policy);
                    break;
                case PolicyType.PublicService:
                    ImplementPublicService(policy);
                    break;
                case PolicyType.Emergency:
                    ImplementEmergency(policy);
                    break;
                case PolicyType.Environmental:
                    ImplementEnvironmental(policy);
                    break;
            }

            // 记录已实施的政策
            activePolicies.Add(new ActivePolicy
            {
                Policy = policy,
                StartTime = Time.time,
                ExpectedDuration = impact.Duration,
                ExpectedImpact = impact
            });

            // 广播政策变更
            EmitEvent("city_policy_changed", policy);

            // 通知所有相关 Agent
            NotifyAffectedAgents(policy);

            currentState = AgentState.Idle;
        }

        private void ImplementTaxChange(PolicyData policy)
        {
            bool isIncrease = (bool)policy.Parameters["is_increase"];
            float rate = policy.Parameters.ContainsKey("tax_rate") ?
                (float)policy.Parameters["tax_rate"] : worldState.TaxRate;

            if (isIncrease)
            {
                worldState.TaxRate = Mathf.Min(0.5f, rate);
                // 市民不开心
                approvalRate -= 5f;
            }
            else
            {
                worldState.TaxRate = Mathf.Max(0.05f, rate);
                // 市民开心
                approvalRate += 3f;
            }

            // 通知经济 Agent
            eventBus.Publish(agentId, "tax_changed", new TaxData
            {
                Rate = worldState.TaxRate
            });
        }

        private void ImplementZoneChange(PolicyData policy)
        {
            var zoneType = (ZoneType)policy.Parameters["zone_type"];
            string direction = policy.Parameters.ContainsKey("direction") ?
                (string)policy.Parameters["direction"] : "center";

            // 创建新区域
            var zone = new ZoneData
            {
                ZoneId = Guid.NewGuid().ToString("N").Substring(0, 8),
                ZoneName = $"{zoneType}区 ({direction})",
                Type = zoneType,
                Cells = CalculateZoneCells(direction),
                Density = 0.5f,
                Desirability = 0.6f
            };

            worldState.RegisterZone(zone);

            EmitEvent("zone_created", zone);
        }

        private void ImplementInfrastructure(PolicyData policy)
        {
            var buildType = (BuildingType)policy.Parameters["build_type"];
            string direction = policy.Parameters.ContainsKey("direction") ?
                (string)policy.Parameters["direction"] : "center";

            // 计算建设成本
            float cost = CalculateBuildCost(buildType);

            if (worldState.SpendBudget(cost, $"建设{buildType}"))
            {
                // 创建建筑
                var building = new BuildingData
                {
                    BuildingId = Guid.NewGuid().ToString("N").Substring(0, 8),
                    BuildingName = $"{buildType} ({direction})",
                    Type = buildType,
                    GridPosition = CalculateBuildPosition(direction),
                    WorldPosition = CalculateWorldPosition(direction),
                    ConstructionProgress = 0f,
                    IsOperational = false,
                    Capacity = GetDefaultCapacity(buildType),
                    MaintenanceCost = cost * 0.01f,
                    Condition = 1f
                };

                worldState.PlaceBuilding(building);

                // 开始施工（通过施工 Agent 或直接模拟）
                EmitEvent("construction_start", building);
            }
            else
            {
                EmitEvent("policy_failed", new
                {
                    policy = policy.Name,
                    reason = "预算不足"
                });
            }
        }

        private void ImplementPublicService(PolicyData policy)
        {
            var serviceType = (BuildingType)policy.Parameters["service_type"];
            ImplementInfrastructure(policy); // 复用基础设施逻辑
        }

        private void ImplementEmergency(PolicyData policy)
        {
            string emergencyType = (string)policy.Parameters["emergency_type"];

            // 广播紧急事件
            EmitEvent("emergency_alert", new
            {
                type = emergencyType,
                policy = policy.Name
            });

            // 启动应急响应
            switch (emergencyType)
            {
                case "evacuation":
                    // 通知市民疏散
                    EmitEvent("evacuation_order", new { zone = "all" });
                    break;
                case "fire":
                    // 通知消防 Agent
                    EmitEvent("fire_dispatch", new { });
                    break;
                case "flood":
                    // 启动防洪措施
                    EmitEvent("flood_response", new { });
                    break;
            }
        }

        private void ImplementEnvironmental(PolicyData policy)
        {
            string action = (string)policy.Parameters["action"];

            switch (action)
            {
                case "green":
                    // 建设公园
                    var park = new BuildingData
                    {
                        BuildingId = Guid.NewGuid().ToString("N").Substring(0, 8),
                        BuildingName = "城市公园",
                        Type = BuildingType.Park,
                        Capacity = 500,
                        MaintenanceCost = 1000f
                    };
                    worldState.PlaceBuilding(park);
                    worldState.CityPollution -= 5f;
                    break;

                case "reduce_pollution":
                    // 限制工业排放
                    EmitEvent("emission_regulation", new { maxEmission = 0.5f });
                    worldState.CityPollution -= 10f;
                    break;

                case "waste_management":
                    // 改善垃圾处理
                    worldState.CityPollution -= 8f;
                    break;
            }

            approvalRate += 5f; // 环保政策通常受欢迎
        }

        // ========== 监控和评估 ==========

        private void MonitorActivePolicies()
        {
            var expiredPolicies = new List<ActivePolicy>();

            foreach (var active in activePolicies)
            {
                float elapsed = Time.time - active.StartTime;

                if (elapsed >= active.ExpectedDuration)
                {
                    // 政策到期，评估最终效果
                    EvaluatePolicyResult(active);
                    expiredPolicies.Add(active);
                }
            }

            foreach (var expired in expiredPolicies)
                activePolicies.Remove(expired);
        }

        private void EvaluatePolicyResult(ActivePolicy active)
        {
            // 对比预期效果和实际效果
            var actual = MeasureActualImpact(active.Policy);
            var expected = active.ExpectedImpact;

            string result = actual.Improvement > expected.Improvement * 0.8f ? "成功" : "部分成功";

            EmitEvent("policy_result", new
            {
                policy = active.Policy.Name,
                result,
                expectedImprovement = expected.Improvement,
                actualImprovement = actual.Improvement
            });
        }

        private ImpactAssessment MeasureActualImpact(PolicyData policy)
        {
            // 测量政策的实际影响
            return new ImpactAssessment
            {
                Improvement = 0.5f, // 简化
                Duration = 0
            };
        }

        private void EvaluateCitizenSatisfaction()
        {
            // 基于多种因素计算满意度
            float satisfaction = 50f;

            // 经济因素
            satisfaction += (1f - worldState.UnemploymentRate) * 20f;

            // 安全因素
            satisfaction += worldState.CitySafety * 0.2f;

            // 环境因素
            satisfaction += (100f - worldState.CityPollution) * 0.1f;

            // 公共服务
            satisfaction += worldState.HospitalCapacity * 0.1f;
            satisfaction += worldState.SchoolCapacity * 0.1f;

            approvalRate = Mathf.Lerp(approvalRate, satisfaction, Time.deltaTime * 0.1f);
        }

        private void EvaluatePolicyEffectiveness()
        {
            // 评估当前政策的整体效果
            memory.SetWorking("approval_rate", approvalRate);
            memory.SetWorking("active_policies", activePolicies.Count);
        }

        private void IdentifyProblems()
        {
            // 识别城市问题，自动生成政策建议
            var problems = new List<string>();

            if (worldState.UnemploymentRate > 0.1f)
                problems.Add("高失业率");
            if (worldState.CityPollution > 60f)
                problems.Add("严重污染");
            if (worldState.CitySafety < 40f)
                problems.Add("治安问题");
            if (worldState.CityHappiness < 40f)
                problems.Add("市民不满");

            if (problems.Count > 0)
            {
                memory.SetWorking("city_problems", problems);
                EmitEvent("problems_detected", problems);
            }
        }

        private void NotifyAffectedAgents(PolicyData policy)
        {
            // 根据政策类型通知相关 Agent
            switch (policy.Type)
            {
                case PolicyType.TaxChange:
                    // 通知所有经济 Agent
                    var economyAgents = AgentManager.Instance.GetAgentsOfType<EconomyAgent>();
                    foreach (var agent in economyAgents)
                    {
                        eventBus.SendTo(agentId, agent.AgentId, "tax_changed", new TaxData
                        {
                            Rate = worldState.TaxRate
                        });
                    }
                    break;

                case PolicyType.Infrastructure:
                    // 通知交通 Agent
                    var trafficAgents = AgentManager.Instance.GetAgentsOfType<TrafficAgent>();
                    foreach (var agent in trafficAgents)
                    {
                        eventBus.SendTo(agentId, agent.AgentId, "infrastructure_changed", policy);
                    }
                    break;

                case PolicyType.Emergency:
                    // 通知所有 Agent
                    eventBus.Publish(agentId, "emergency_alert", policy);
                    break;
            }
        }

        // ========== 辅助方法 ==========

        private float CalculateBuildCost(BuildingType type)
        {
            return type switch
            {
                BuildingType.Road => 50000f,
                BuildingType.Highway => 200000f,
                BuildingType.Subway => 500000f,
                BuildingType.BusStation => 80000f,
                BuildingType.Hospital => 300000f,
                BuildingType.School => 200000f,
                BuildingType.PoliceStation => 150000f,
                BuildingType.FireStation => 150000f,
                BuildingType.Park => 100000f,
                BuildingType.PowerPlant => 400000f,
                BuildingType.WaterPlant => 350000f,
                _ => 100000f
            };
        }

        private int GetDefaultCapacity(BuildingType type)
        {
            return type switch
            {
                BuildingType.Hospital => 200,
                BuildingType.School => 500,
                BuildingType.PoliceStation => 50,
                BuildingType.FireStation => 30,
                BuildingType.Park => 1000,
                BuildingType.BusStation => 200,
                _ => 100
            };
        }

        private List<Vector2Int> CalculateZoneCells(string direction)
        {
            // 简化：返回固定大小的区域
            var cells = new List<Vector2Int>();
            Vector2Int center = direction switch
            {
                "东" => new Vector2Int(50, 0),
                "西" => new Vector2Int(-50, 0),
                "南" => new Vector2Int(0, -50),
                "北" => new Vector2Int(0, 50),
                _ => new Vector2Int(0, 0)
            };

            for (int x = -5; x <= 5; x++)
                for (int y = -5; y <= 5; y++)
                    cells.Add(new Vector2Int(center.x + x, center.y + y));

            return cells;
        }

        private Vector2Int CalculateBuildPosition(string direction)
        {
            return direction switch
            {
                "东" => new Vector2Int(60, 0),
                "西" => new Vector2Int(-60, 0),
                "南" => new Vector2Int(0, -60),
                "北" => new Vector2Int(0, 60),
                _ => new Vector2Int(0, 0)
            };
        }

        private Vector3 CalculateWorldPosition(string direction)
        {
            var grid = CalculateBuildPosition(direction);
            return new Vector3(grid.x * 2f, 0, grid.y * 2f);
        }

        private void InitializePolicyTemplates()
        {
            // 初始化政策模板
            policyTemplates["减税"] = new PolicyTemplate
            {
                Name = "减税政策",
                Type = PolicyType.TaxChange,
                Description = "降低税率以刺激经济",
                DefaultParameters = new Dictionary<string, object> { { "is_increase", false } },
                ExpectedImpact = new ImpactAssessment { Improvement = 0.3f, Duration = 3600f }
            };

            policyTemplates["建医院"] = new PolicyTemplate
            {
                Name = "建设医院",
                Type = PolicyType.PublicService,
                Description = "新建医疗机构",
                DefaultParameters = new Dictionary<string, object> { { "service_type", BuildingType.Hospital } },
                ExpectedImpact = new ImpactAssessment { Improvement = 0.4f, Duration = 7200f }
            };
        }

        // ========== 事件处理 ==========

        private void OnNaturalLanguageCommand(EventMessage msg)
        {
            if (msg.Data is string command)
            {
                var policy = ParseNaturalLanguage(command);

                if (policy.Type != PolicyType.Unknown)
                {
                    // 先评估影响
                    var impact = impactModel.Evaluate(policy, worldState);

                    // 通知玩家评估结果
                    EmitEvent("policy_evaluation", new
                    {
                        policy = policy.Name,
                        impact = impact,
                        approvalEffect = impact.ApprovalChange
                    });

                    // 加入待执行队列
                    pendingPolicies.Enqueue(policy);
                }
                else
                {
                    EmitEvent("policy_parse_failed", new
                    {
                        input = command,
                        reason = policy.RejectionReason
                    });
                }
            }
        }

        private void OnPolicyFeedback(EventMessage msg)
        {
            // 收集政策反馈
        }

        private void OnCitizenComplaint(EventMessage msg)
        {
            // 市民投诉，降低满意度
            approvalRate -= 1f;
        }

        private void OnInterestRateSuggestion(EventMessage msg)
        {
            // 来自经济 Agent 的建议
        }

        private void OnInfrastructureRequest(EventMessage msg)
        {
            // 来自经济 Agent 的基建请求
        }
    }

    // ========== 数据结构 ==========

    public enum PolicyType
    {
        Unknown,
        TaxChange,
        ZoneChange,
        Infrastructure,
        PublicService,
        Emergency,
        Environmental,
        Curfew,
        TaxIncrease,
        PublicEvent
    }

    public enum PolicyIntent
    {
        Unknown,
        TaxChange,
        ZoneChange,
        Infrastructure,
        PublicService,
        Emergency,
        Environmental
    }

    [System.Serializable]
    public class PolicyData
    {
        public string PolicyId;
        public string Name;
        public string Description;
        public string RawInput;
        public PolicyType Type;
        public float Urgency;
        public float Timestamp;
        public Dictionary<string, object> Parameters = new Dictionary<string, object>();
        public string RejectionReason;
    }

    [System.Serializable]
    public class ActivePolicy
    {
        public PolicyData Policy;
        public float StartTime;
        public float ExpectedDuration;
        public ImpactAssessment ExpectedImpact;
    }

    [System.Serializable]
    public class PolicyTemplate
    {
        public string Name;
        public PolicyType Type;
        public string Description;
        public Dictionary<string, object> DefaultParameters;
        public ImpactAssessment ExpectedImpact;
    }

    [System.Serializable]
    public class ImpactAssessment
    {
        public float Feasibility;      // 可行性 0-1
        public float Improvement;      // 预期改善 0-1
        public float Duration;         // 持续时间（秒）
        public float Cost;             // 成本
        public float ApprovalChange;   // 支持率变化
        public string RejectionReason; // 拒绝原因
        public string AlternativeSuggestion; // 替代建议
    }

    /// <summary>
    /// 影响评估模型
    /// </summary>
    public class ImpactModel
    {
        public ImpactAssessment Evaluate(PolicyData policy, WorldState world)
        {
            var impact = new ImpactAssessment();

            switch (policy.Type)
            {
                case PolicyType.TaxChange:
                    EvaluateTaxImpact(policy, world, impact);
                    break;
                case PolicyType.Infrastructure:
                    EvaluateInfrastructureImpact(policy, world, impact);
                    break;
                case PolicyType.PublicService:
                    EvaluatePublicServiceImpact(policy, world, impact);
                    break;
                case PolicyType.Emergency:
                    EvaluateEmergencyImpact(policy, world, impact);
                    break;
                case PolicyType.Environmental:
                    EvaluateEnvironmentalImpact(policy, world, impact);
                    break;
                default:
                    impact.Feasibility = 0.5f;
                    break;
            }

            return impact;
        }

        private void EvaluateTaxImpact(PolicyData policy, WorldState world, ImpactAssessment impact)
        {
            bool isIncrease = policy.Parameters.ContainsKey("is_increase") &&
                              (bool)policy.Parameters["is_increase"];

            if (isIncrease)
            {
                // 增税：增加收入但降低满意度
                impact.Feasibility = 0.9f;
                impact.Improvement = 0.3f;
                impact.Cost = 0;
                impact.ApprovalChange = -5f;
                impact.Duration = 3600f;
            }
            else
            {
                // 减税：减少收入但提高满意度
                if (world.CityBudget < world.TotalPopulation * 100)
                {
                    impact.Feasibility = 0.3f;
                    impact.RejectionReason = "预算不足，无法承受减税";
                    impact.AlternativeSuggestion = "建议先削减非必要开支再减税";
                }
                else
                {
                    impact.Feasibility = 0.8f;
                    impact.Improvement = 0.4f;
                    impact.Cost = world.CityBudget * 0.1f;
                    impact.ApprovalChange = 5f;
                    impact.Duration = 3600f;
                }
            }
        }

        private void EvaluateInfrastructureImpact(PolicyData policy, WorldState world, ImpactAssessment impact)
        {
            var buildType = policy.Parameters.ContainsKey("build_type") ?
                (BuildingType)policy.Parameters["build_type"] : BuildingType.Road;

            float cost = buildType switch
            {
                BuildingType.Subway => 500000f,
                BuildingType.Highway => 200000f,
                _ => 100000f
            };

            if (world.CityBudget >= cost)
            {
                impact.Feasibility = 0.9f;
                impact.Improvement = 0.5f;
                impact.Cost = cost;
                impact.ApprovalChange = 3f;
                impact.Duration = 7200f;
            }
            else
            {
                impact.Feasibility = 0.2f;
                impact.RejectionReason = $"预算不足（需要 {cost:C0}，可用 {world.CityBudget:C0}）";
                impact.AlternativeSuggestion = "建议分阶段建设或申请贷款";
            }
        }

        private void EvaluatePublicServiceImpact(PolicyData policy, WorldState world, ImpactAssessment impact)
        {
            impact.Feasibility = 0.8f;
            impact.Improvement = 0.4f;
            impact.Cost = 200000f;
            impact.ApprovalChange = 5f;
            impact.Duration = 7200f;
        }

        private void EvaluateEmergencyImpact(PolicyData policy, WorldState world, ImpactAssessment impact)
        {
            impact.Feasibility = 1f; // 紧急政策总是可行
            impact.Improvement = 0.8f;
            impact.Cost = 50000f;
            impact.ApprovalChange = 0f;
            impact.Duration = 1800f;
        }

        private void EvaluateEnvironmentalImpact(PolicyData policy, WorldState world, ImpactAssessment impact)
        {
            impact.Feasibility = 0.7f;
            impact.Improvement = 0.3f;
            impact.Cost = 100000f;
            impact.ApprovalChange = 5f;
            impact.Duration = 3600f;
        }
    }
}

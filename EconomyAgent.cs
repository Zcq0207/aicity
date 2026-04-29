// ============================================================================
// EconomyAgent.cs — 经济 Agent：管理城市经济系统
// ============================================================================
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using AICity.Core;

namespace AICity.Agents
{
    /// <summary>
    /// 经济 Agent — 负责城市经济运行的智能体
    /// 管理：市场供需、物价、就业、税收、产业发展
    /// 多个 EconomicAgent 负责不同行业，协作形成完整经济系统
    /// </summary>
    public class EconomyAgent : AgentBase
    {
        [Header("管辖行业")]
        [SerializeField] private IndustryType industry;
        [SerializeField] private string industryName;

        [Header("市场状态")]
        [SerializeField] private float supply;          // 供给量
        [SerializeField] private float demand;          // 需求量
        [SerializeField] private float price;           // 当前价格
        [SerializeField] private float basePrice;       // 基准价格
        [SerializeField] private float inflation;       // 行业通胀率

        [Header("就业")]
        [SerializeField] private int totalJobs;         // 总岗位数
        [SerializeField] private int filledJobs;        // 已填充岗位
        [SerializeField] private float averageWage;     // 平均工资

        [Header("企业")]
        [SerializeField] private List<BusinessData> businesses = new List<BusinessData>();

        // 价格历史（用于趋势分析）
        private Queue<float> priceHistory = new Queue<float>();
        private const int MAX_PRICE_HISTORY = 168; // 一周的小时数

        // 供需模型参数
        private float elasticity = 0.5f; // 需求弹性
        private float supplyGrowthRate = 0.01f;

        protected override void InitializeMemory()
        {
            Remember("industry", industry, 0.9f);
            Remember("base_price", basePrice, 0.8f);
            price = basePrice;
        }

        protected override void SubscribeEvents()
        {
            eventBus.Subscribe("citizen_consumption", OnConsumption);
            eventBus.Subscribe("business_created", OnBusinessCreated);
            eventBus.Subscribe("business_closed", OnBusinessClosed);
            eventBus.Subscribe("tax_changed", OnTaxChanged);
            eventBus.Subscribe("trade_request", OnTradeRequest);
            eventBus.Subscribe("collaboration_request", OnCollaborationRequest);
        }

        protected override void UnsubscribeEvents()
        {
            eventBus.Unsubscribe("citizen_consumption", OnConsumption);
            eventBus.Unsubscribe("business_created", OnBusinessCreated);
            eventBus.Unsubscribe("business_closed", OnBusinessClosed);
            eventBus.Unsubscribe("tax_changed", OnTaxChanged);
            eventBus.Unsubscribe("trade_request", OnTradeRequest);
            eventBus.Unsubscribe("collaboration_request", OnCollaborationRequest);
        }

        // ========== 核心循环 ==========

        protected override void Perception()
        {
            // 1. 监测市场需求
            MeasureDemand();

            // 2. 监测供给
            MeasureSupply();

            // 3. 分析竞争环境
            AnalyzeCompetition();

            // 4. 读取经济指标
            ReadEconomicIndicators();
        }

        protected override void Think()
        {
            // 1. 供需分析
            float supplyDemandRatio = supply / Mathf.Max(0.01f, demand);
            memory.SetWorking("supply_demand_ratio", supplyDemandRatio);

            // 2. 价格趋势分析
            AnalyzePriceTrend();

            // 3. 就业市场分析
            AnalyzeEmployment();

            // 4. 评估是否需要干预
            bool needsIntervention = EvaluateInterventionNeed();
            if (needsIntervention)
            {
                currentState = AgentState.Planning;
            }
        }

        protected override void Decide()
        {
            if (currentState != AgentState.Planning)
                return;

            float ratio = memory.Recall<float>("supply_demand_ratio");

            if (ratio > 1.5f) // 供过于求
            {
                currentTask = new AgentTask
                {
                    TaskType = "reduce_supply",
                    Description = "供给过剩，需要减产或刺激需求",
                    Priority = 0.7f
                };
            }
            else if (ratio < 0.5f) // 供不应求
            {
                currentTask = new AgentTask
                {
                    TaskType = "increase_supply",
                    Description = "需求旺盛，需要扩大生产",
                    Priority = 0.8f
                };
            }
            else if (inflation > 0.05f) // 高通胀
            {
                currentTask = new AgentTask
                {
                    TaskType = "control_inflation",
                    Description = "通胀过高，需要调控价格",
                    Priority = 0.9f
                };
            }
            else if (UnemploymentRate > 0.1f) // 高失业
            {
                currentTask = new AgentTask
                {
                    TaskType = "create_jobs",
                    Description = "失业率高，需要创造就业",
                    Priority = 0.85f
                };
            }
            else
            {
                currentTask = new AgentTask
                {
                    TaskType = "monitor",
                    Description = "经济稳定，持续监控",
                    Priority = 0.2f
                };
            }

            currentState = AgentState.Executing;
        }

        protected override void Act()
        {
            if (currentTask == null) return;

            switch (currentTask.TaskType)
            {
                case "reduce_supply":
                    ActReduceSupply();
                    break;
                case "increase_supply":
                    ActIncreaseSupply();
                    break;
                case "control_inflation":
                    ActControlInflation();
                    break;
                case "create_jobs":
                    ActCreateJobs();
                    break;
                case "monitor":
                    ActMonitor();
                    break;
            }
        }

        // ========== 感知实现 ==========

        private void MeasureDemand()
        {
            // 基于市民消费行为计算需求
            float populationDemand = worldState.TotalPopulation * GetPerCapitaDemand();
            float priceEffect = Mathf.Pow(basePrice / Mathf.Max(0.01f, price), elasticity);

            demand = populationDemand * priceEffect;

            // 季节性调整
            demand *= GetSeasonalFactor();
        }

        private void MeasureSupply()
        {
            // 基于企业产能计算供给
            float totalCapacity = businesses.Sum(b => b.ProductionCapacity);
            float efficiency = businesses.Count > 0 ?
                businesses.Average(b => b.Efficiency) : 0.5f;

            supply = totalCapacity * efficiency;

            // 自然增长
            supply *= (1f + supplyGrowthRate * Time.deltaTime);
        }

        private void AnalyzeCompetition()
        {
            // 分析行业竞争格局
            int businessCount = businesses.Count;
            float marketConcentration = CalculateHHI();

            memory.SetWorking("business_count", businessCount);
            memory.SetWorking("market_concentration", marketConcentration);

            // 垄断检测
            if (marketConcentration > 0.7f && businessCount > 1)
            {
                EmitEvent("monopoly_detected", new
                {
                    industry,
                    concentration = marketConcentration
                });
            }
        }

        private void ReadEconomicIndicators()
        {
            // 读取全球经济指标
            UnemploymentRate = CalculateUnemploymentRate();
            inflation = CalculateInflation();

            memory.SetWorking("unemployment", UnemploymentRate);
            memory.SetWorking("inflation", inflation);
        }

        // ========== 决策分析 ==========

        private void AnalyzePriceTrend()
        {
            // 记录价格历史
            priceHistory.Enqueue(price);
            if (priceHistory.Count > MAX_PRICE_HISTORY)
                priceHistory.Dequeue();

            // 计算价格趋势
            if (priceHistory.Count >= 24)
            {
                var prices = priceHistory.ToArray();
                float recentAvg = prices.Skip(prices.Length - 24).Average();
                float olderAvg = prices.Take(prices.Length - 24).Average();

                float trend = (recentAvg - olderAvg) / olderAvg;
                memory.SetWorking("price_trend", trend);
            }
        }

        private void AnalyzeEmployment()
        {
            float employmentRate = filledJobs / Mathf.Max(1f, totalJobs);
            memory.SetWorking("employment_rate", employmentRate);

            // 工资压力分析
            if (employmentRate > 0.95f)
            {
                // 劳动力紧缺，工资上涨压力
                averageWage *= (1f + 0.01f * Time.deltaTime);
            }
            else if (employmentRate < 0.8f)
            {
                // 劳动力过剩，工资下降压力
                averageWage *= (1f - 0.005f * Time.deltaTime);
            }
        }

        private bool EvaluateInterventionNeed()
        {
            float ratio = memory.Recall<float>("supply_demand_ratio");
            float trend = memory.Recall<float>("price_trend");

            // 供需失衡、价格剧烈波动、高失业高通胀都需要干预
            return ratio > 1.5f || ratio < 0.5f ||
                   Mathf.Abs(trend) > 0.1f ||
                   UnemploymentRate > 0.08f ||
                   inflation > 0.04f;
        }

        // ========== 行为实现 ==========

        private void ActReduceSupply()
        {
            // 1. 关闭低效企业
            var inefficientBusinesses = businesses
                .OrderBy(b => b.Efficiency)
                .Take(Mathf.Max(1, businesses.Count / 10))
                .ToList();

            foreach (var business in inefficientBusinesses)
            {
                business.ProductionCapacity *= 0.8f;
            }

            // 2. 发出减产信号
            EmitEvent("production_cut", new
            {
                industry,
                reduction = 0.1f
            });

            // 3. 价格自动调整（市场机制）
            price *= 0.95f;

            currentState = AgentState.Idle;
        }

        private void ActIncreaseSupply()
        {
            // 1. 鼓励现有企业扩产
            foreach (var business in businesses)
            {
                business.ProductionCapacity *= 1.1f;
            }

            // 2. 发出招工信号
            EmitEvent("hiring_surge", new
            {
                industry,
                newJobs = Mathf.CeilToInt(totalJobs * 0.1f),
                wage = averageWage
            });

            // 3. 价格调整
            price *= 1.05f;

            currentState = AgentState.Idle;
        }

        private void ActControlInflation()
        {
            // 1. 限制价格上涨
            float maxPrice = basePrice * (1f + 0.03f); // 最高涨 3%
            price = Mathf.Min(price, maxPrice);

            // 2. 增加供给
            foreach (var business in businesses)
            {
                business.ProductionCapacity *= 1.05f;
            }

            // 3. 向政策 Agent 建议加息
            EmitEvent("suggest_interest_rate_hike", new
            {
                industry,
                currentInflation = inflation,
                suggestedRate = inflation * 0.5f
            });

            currentState = AgentState.Idle;
        }

        private void ActCreateJobs()
        {
            // 1. 鼓励创业
            EmitEvent("entrepreneurship_incentive", new
            {
                industry,
                subsidy = averageWage * 3f, // 3 个月工资补贴
                duration = 6 // 6 个月
            });

            // 2. 基础设施投资（创造就业）
            EmitEvent("infrastructure_investment", new
            {
                industry,
                investment = worldState.CityBudget * 0.05f,
                expectedJobs = Mathf.CeilToInt(UnemploymentRate * worldState.TotalPopulation * 0.1f)
            });

            // 3. 职业培训
            EmitEvent("vocational_training", new
            {
                industry,
                trainingSlots = Mathf.CeilToInt(UnemploymentRate * 100)
            });

            currentState = AgentState.Idle;
        }

        private void ActMonitor()
        {
            // 持续监控，定期报告
            if (Time.frameCount % 300 == 0) // 每 5 秒（60fps）
            {
                EmitEvent("economic_report", new
                {
                    industry,
                    supply,
                    demand,
                    price,
                    inflation,
                    unemployment = UnemploymentRate,
                    businessCount = businesses.Count
                });
            }

            currentState = AgentState.Idle;
        }

        // ========== 辅助方法 ==========

        private float GetPerCapitaDemand()
        {
            return industry switch
            {
                IndustryType.Food => 1.0f,
                IndustryType.Housing => 0.3f,
                IndustryType.Retail => 0.5f,
                IndustryType.Healthcare => 0.2f,
                IndustryType.Education => 0.15f,
                IndustryType.Entertainment => 0.3f,
                IndustryType.Technology => 0.1f,
                IndustryType.Manufacturing => 0.05f,
                _ => 0.2f
            };
        }

        private float GetSeasonalFactor()
        {
            int month = worldState.CurrentMonth;
            return industry switch
            {
                IndustryType.Food => 1f + 0.1f * Mathf.Sin(month * Mathf.PI / 6f),
                IndustryType.Entertainment => month >= 6 && month <= 8 ? 1.3f : 0.9f,
                IndustryType.Retail => month == 12 || month == 1 ? 1.5f : 1f,
                _ => 1f
            };
        }

        private float CalculateHHI()
        {
            // 赫芬达尔指数，衡量市场集中度
            if (businesses.Count == 0) return 0f;

            float totalRevenue = businesses.Sum(b => b.Revenue);
            if (totalRevenue == 0) return 0f;

            float hhi = 0f;
            foreach (var b in businesses)
            {
                float share = b.Revenue / totalRevenue;
                hhi += share * share;
            }
            return hhi;
        }

        private float CalculateUnemploymentRate()
        {
            if (totalJobs == 0) return 1f;
            return 1f - (float)filledJobs / totalJobs;
        }

        private float CalculateInflation()
        {
            if (priceHistory.Count < 24) return 0f;

            var prices = priceHistory.ToArray();
            float current = prices[prices.Length - 1];
            float yesterday = prices[prices.Length - 24];

            return (current - yesterday) / yesterday;
        }

        // ========== 事件处理 ==========

        private void OnConsumption(EventMessage msg)
        {
            if (msg.Data is ConsumptionData data && data.Industry == industry)
            {
                demand += data.Amount;
                supply -= data.Amount;
            }
        }

        private void OnBusinessCreated(EventMessage msg)
        {
            if (msg.Data is BusinessData business && business.Industry == industry)
            {
                businesses.Add(business);
                totalJobs += business.EmployeeCount;
            }
        }

        private void OnBusinessClosed(EventMessage msg)
        {
            if (msg.Data is BusinessData business && business.Industry == industry)
            {
                businesses.Remove(business);
                totalJobs -= business.EmployeeCount;
                filledJobs -= business.EmployeeCount;
            }
        }

        private void OnTaxChanged(EventMessage msg)
        {
            // 税率变化影响企业利润和消费者价格
            if (msg.Data is TaxData tax)
            {
                foreach (var business in businesses)
                {
                    business.TaxRate = tax.Rate;
                }
            }
        }

        private void OnTradeRequest(EventMessage msg)
        {
            // 处理跨行业交易请求
        }

        private void OnCollaborationRequest(EventMessage msg)
        {
            if (msg.Data is CollaborationMessage collab && collab.ToAgentId == agentId)
            {
                var request = collab.Request;
                if (request.RequestType == "supply_request")
                {
                    // 其他行业请求供给
                    float availableSupply = supply * 0.1f; // 最多提供 10%
                    if (availableSupply > 0)
                    {
                        eventBus.SendTo(agentId, collab.FromAgentId, "supply_response", new
                        {
                            available = availableSupply,
                            price = price
                        });
                    }
                }
            }
        }
    }

    // ========== 数据结构 ==========

    public enum IndustryType
    {
        Food,
        Housing,
        Retail,
        Healthcare,
        Education,
        Entertainment,
        Technology,
        Manufacturing,
        Energy,
        Transportation
    }

    [System.Serializable]
    public class BusinessData
    {
        public string BusinessId;
        public string BusinessName;
        public IndustryType Industry;
        public float ProductionCapacity;
        public float Efficiency;
        public float Revenue;
        public float Profit;
        public int EmployeeCount;
        public float TaxRate;
        public float GrowthRate;
    }

    [System.Serializable]
    public class ConsumptionData
    {
        public string CitizenId;
        public IndustryType Industry;
        public float Amount;
    }

    [System.Serializable]
    public class TaxData
    {
        public float Rate;
        public IndustryType[] AffectedIndustries;
    }
}

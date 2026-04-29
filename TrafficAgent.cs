// ============================================================================
// TrafficAgent.cs — 交通 Agent：管理交通流量、信号灯、公共交通
// ============================================================================
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using AICity.Core;

namespace AICity.Agents
{
    /// <summary>
    /// 交通 Agent — 负责城市交通系统的智能调度
    /// 管理：红绿灯时序、公交线路、交通拥堵预测、事故响应
    /// 多个 TrafficAgent 协作覆盖整个城市路网
    /// </summary>
    public class TrafficAgent : AgentBase
    {
        [Header("管辖区域")]
        [SerializeField] private string zoneId;
        [SerializeField] private float zoneRadius = 200f;

        [Header("交通状态")]
        [SerializeField] private float congestionLevel;   // 0-1 拥堵程度
        [SerializeField] private float averageSpeed;       // 平均车速
        [SerializeField] private int vehicleCount;         // 区域车辆数
        [SerializeField] private int accidentCount;        // 事故数量

        [Header("信号灯控制")]
        [SerializeField] private List<TrafficLight> managedLights = new List<TrafficLight>();

        [Header("公交系统")]
        [SerializeField] private List<BusRoute> busRoutes = new List<BusRoute>();

        // 交通历史数据（用于预测）
        private float[] congestionHistory = new float[24]; // 24 小时历史
        private float[] flowPrediction = new float[24];    // 预测流量

        // 邻居交通 Agent（用于协作）
        private List<TrafficAgent> neighborAgents = new List<TrafficAgent>();

        // ========== 路段数据 ==========
        private Dictionary<string, RoadSegment> roadSegments = new Dictionary<string, RoadSegment>();

        protected override void InitializeMemory()
        {
            Remember("zone_id", zoneId, 0.9f);
            Remember("zone_radius", zoneRadius, 0.8f);

            // 初始化历史数据
            for (int i = 0; i < 24; i++)
                congestionHistory[i] = 0.3f; // 默认低拥堵
        }

        protected override void SubscribeEvents()
        {
            eventBus.Subscribe("vehicle_spawned", OnVehicleSpawned);
            eventBus.Subscribe("vehicle_despawned", OnVehicleDespawned);
            eventBus.Subscribe("accident_reported", OnAccidentReported);
            eventBus.Subscribe("road_closed", OnRoadClosed);
            eventBus.Subscribe("traffic_request", OnTrafficRequest);
            eventBus.Subscribe("collaboration_request", OnCollaborationRequest);
        }

        protected override void UnsubscribeEvents()
        {
            eventBus.Unsubscribe("vehicle_spawned", OnVehicleSpawned);
            eventBus.Unsubscribe("vehicle_despawned", OnVehicleDespawned);
            eventBus.Unsubscribe("accident_reported", OnAccidentReported);
            eventBus.Unsubscribe("road_closed", OnRoadClosed);
            eventBus.Unsubscribe("traffic_request", OnTrafficRequest);
            eventBus.Unsubscribe("collaboration_request", OnCollaborationRequest);
        }

        // ========== 核心循环 ==========

        protected override void Perception()
        {
            // 1. 扫描区域内车辆
            ScanVehicles();

            // 2. 检测拥堵
            CalculateCongestion();

            // 3. 检查信号灯状态
            CheckTrafficLights();

            // 4. 检查公交运行状态
            CheckBusRoutes();

            // 5. 与邻居 Agent 交换信息
            ExchangeWithNeighbors();
        }

        protected override void Think()
        {
            // 1. 分析当前交通状况
            AnalyzeTrafficCondition();

            // 2. 预测未来流量
            PredictTrafficFlow();

            // 3. 评估是否需要调整
            if (ShouldAdjustSignalTiming())
            {
                currentState = AgentState.Planning;
            }

            if (ShouldRequestCollaboration())
            {
                RequestNeighborHelp();
            }
        }

        protected override void Decide()
        {
            if (currentState != AgentState.Planning)
                return;

            // 决策优先级
            if (accidentCount > 0)
            {
                // 事故响应优先
                currentTask = new AgentTask
                {
                    TaskType = "accident_response",
                    Description = "响应交通事故，调整信号灯引导绕行",
                    Priority = 1f
                };
            }
            else if (congestionLevel > 0.7f)
            {
                // 严重拥堵，调整信号灯
                currentTask = new AgentTask
                {
                    TaskType = "signal_optimization",
                    Description = "优化信号灯时序缓解拥堵",
                    Priority = 0.8f
                };
            }
            else if (worldState.IsRushHour())
            {
                // 高峰期，启用潮汐车道
                currentTask = new AgentTask
                {
                    TaskType = "tidal_lane",
                    Description = "启用潮汐车道应对高峰流量",
                    Priority = 0.6f
                };
            }
            else
            {
                // 常规优化
                currentTask = new AgentTask
                {
                    TaskType = "routine_optimization",
                    Description = "常规交通信号优化",
                    Priority = 0.3f
                };
            }

            currentState = AgentState.Executing;
        }

        protected override void Act()
        {
            if (currentTask == null) return;

            switch (currentTask.TaskType)
            {
                case "accident_response":
                    HandleAccidentResponse();
                    break;
                case "signal_optimization":
                    OptimizeSignalTiming();
                    break;
                case "tidal_lane":
                    EnableTidalLane();
                    break;
                case "routine_optimization":
                    RoutineOptimization();
                    break;
            }
        }

        // ========== 感知实现 ==========

        private void ScanVehicles()
        {
            var vehicles = GetEntitiesInRange<Vehicle>(zoneRadius);
            vehicleCount = vehicles.Count;

            // 统计各路段车辆数
            foreach (var segment in roadSegments.Values)
            {
                segment.CurrentVehicles = 0;
            }

            foreach (var vehicle in vehicles)
            {
                string segmentId = vehicle.CurrentSegmentId;
                if (roadSegments.ContainsKey(segmentId))
                {
                    roadSegments[segmentId].CurrentVehicles++;
                }
            }
        }

        private void CalculateCongestion()
        {
            if (roadSegments.Count == 0) return;

            float totalCongestion = 0f;
            foreach (var segment in roadSegments.Values)
            {
                float segmentCongestion = (float)segment.CurrentVehicles / Mathf.Max(1, segment.Capacity);
                segment.Congestion = Mathf.Clamp01(segmentCongestion);
                totalCongestion += segment.Congestion;
            }

            congestionLevel = totalCongestion / roadSegments.Count;
            averageSpeed = Mathf.Lerp(60f, 5f, congestionLevel); // 60km/h → 5km/h
        }

        private void CheckTrafficLights()
        {
            foreach (var light in managedLights)
            {
                if (light.IsMalfunctioning)
                {
                    EmitEvent("traffic_light_broken", new { lightId = light.LightId, position = light.Position });
                }
            }
        }

        private void CheckBusRoutes()
        {
            foreach (var route in busRoutes)
            {
                // 检查公交是否准时
                float delay = CalculateBusDelay(route);
                if (delay > 5f) // 延误超过 5 分钟
                {
                    // 调整信号灯优先公交通行
                    PrioritizeBusAtIntersections(route);
                }
            }
        }

        private void ExchangeWithNeighbors()
        {
            // 与邻居 Agent 交换交通信息
            foreach (var neighbor in neighborAgents)
            {
                // 共享边界路段的拥堵信息
                var boundarySegments = GetBoundarySegments(neighbor);
                foreach (var segment in boundarySegments)
                {
                    neighbor.ReceiveSharedTrafficInfo(segment.SegmentId, segment.Congestion);
                }
            }
        }

        // ========== 决策实现 ==========

        private void AnalyzeTrafficCondition()
        {
            // 存储当前拥堵到历史
            int hour = Mathf.FloorToInt(worldState.GameTime);
            congestionHistory[hour % 24] = congestionLevel;

            // 更新世界状态
            memory.SetWorking("congestion", congestionLevel);
            memory.SetWorking("vehicle_count", vehicleCount);
            memory.SetWorking("average_speed", averageSpeed);
        }

        private void PredictTrafficFlow()
        {
            // 基于历史数据预测未来 3 小时流量
            int currentHour = Mathf.FloorToInt(worldState.GameTime);
            for (int i = 0; i < 3; i++)
            {
                int futureHour = (currentHour + i + 1) % 24;
                // 简单预测：历史数据 + 时间模式修正
                float prediction = congestionHistory[futureHour];

                // 高峰期修正
                if (futureHour >= 7 && futureHour <= 9)
                    prediction *= 1.3f;
                if (futureHour >= 17 && futureHour <= 19)
                    prediction *= 1.2f;

                // 天气修正（如果下雨）
                if (worldState.CityPollution > 50f) // 用污染代替天气的简化
                    prediction *= 1.15f;

                flowPrediction[futureHour] = Mathf.Clamp01(prediction);
            }

            // 存储预测结果
            memory.SetWorking("flow_prediction", flowPrediction);
        }

        private bool ShouldAdjustSignalTiming()
        {
            // 拥堵变化超过阈值需要调整
            float recentChange = Mathf.Abs(congestionHistory[Mathf.FloorToInt(worldState.GameTime) % 24] - congestionLevel);
            return recentChange > 0.15f || congestionLevel > 0.6f;
        }

        private bool ShouldRequestCollaboration()
        {
            // 严重拥堵且邻居有余力时请求协作
            return congestionLevel > 0.8f &&
                   neighborAgents.Any(n => n.congestionLevel < 0.4f);
        }

        private void RequestNeighborHelp()
        {
            // 找到最空闲的邻居
            var bestNeighbor = neighborAgents
                .OrderBy(n => n.congestionLevel)
                .FirstOrDefault();

            if (bestNeighbor != null)
            {
                RequestCollaboration(bestNeighbor.AgentId, new CollaborationRequest
                {
                    RequestType = "traffic_diversion",
                    Description = $"请求分流车辆，当前拥堵 {congestionLevel:P0}",
                    Urgency = congestionLevel
                });
            }
        }

        // ========== 行为实现 ==========

        private void HandleAccidentResponse()
        {
            // 1. 通知相邻交通 Agent
            foreach (var neighbor in neighborAgents)
            {
                eventBus.SendTo(agentId, neighbor.AgentId, "accident_nearby", new
                {
                    zoneId,
                    accidentCount
                });
            }

            // 2. 调整信号灯，引导绕行
            foreach (var light in managedLights)
            {
                // 事故方向红灯延长
                light.ExtendRedPhase(30f);
            }

            // 3. 通知市民避开该区域
            EmitEvent("area_avoidance", new
            {
                zoneId,
                radius = zoneRadius,
                reason = "accident"
            });

            // 记录到记忆
            Remember($"accident_{Time.frameCount}", new
            {
                time = worldState.GameTime,
                position = transform.position
            }, 0.9f);

            currentState = AgentState.Idle;
        }

        private void OptimizeSignalTiming()
        {
            // 基于实时流量优化信号灯时序
            foreach (var light in managedLights)
            {
                // 获取各方向的车流量
                var flows = GetIntersectionFlows(light);

                // 计算最优绿灯时长
                float totalFlow = flows.Sum();
                if (totalFlow > 0)
                {
                    foreach (var flow in flows)
                    {
                        float greenRatio = flow / totalFlow;
                        float greenDuration = Mathf.Lerp(15f, 60f, greenRatio);
                        light.SetGreenDuration(flow.Key, greenDuration);
                    }
                }
            }

            // 协调相邻信号灯（绿波）
            CoordinateGreenWave();

            currentState = AgentState.Idle;
        }

        private void EnableTidalLane()
        {
            // 潮汐车道：根据高峰方向调整车道方向
            bool isMorningRush = worldState.GameTime >= 7f && worldState.GameTime <= 9f;

            foreach (var segment in roadSegments.Values)
            {
                if (segment.HasTidalLane)
                {
                    // 早高峰：进城方向多一条车道
                    // 晚高峰：出城方向多一条车道
                    segment.TidalLaneDirection = isMorningRush ?
                        TidalDirection.Inbound : TidalDirection.Outbound;
                }
            }

            EmitEvent("tidal_lane_changed", new
            {
                zoneId,
                direction = isMorningRush ? "inbound" : "outbound"
            });

            currentState = AgentState.Idle;
        }

        private void RoutineOptimization()
        {
            // 常规优化：根据历史数据微调
            int hour = Mathf.FloorToInt(worldState.GameTime);

            foreach (var light in managedLights)
            {
                // 根据历史同期数据调整基准时长
                float historicalCongestion = congestionHistory[hour];
                float baseDuration = Mathf.Lerp(30f, 45f, historicalCongestion);
                light.SetBaseCycleDuration(baseDuration);
            }

            currentState = AgentState.Idle;
        }

        // ========== 辅助方法 ==========

        private Dictionary<string, float> GetIntersectionFlows(TrafficLight light)
        {
            // 获取交叉口各方向的车流量
            var flows = new Dictionary<string, float>();
            // 简化实现：基于路段车辆数
            flows["north"] = GetFlowFromDirection(light.Position, Vector3.forward);
            flows["south"] = GetFlowFromDirection(light.Position, Vector3.back);
            flows["east"] = GetFlowFromDirection(light.Position, Vector3.right);
            flows["west"] = GetFlowFromDirection(light.Position, Vector3.left);
            return flows;
        }

        private float GetFlowFromDirection(Vector3 intersection, Vector3 direction)
        {
            // 从指定方向统计接近交叉口的车辆数
            float flow = 0f;
            foreach (var segment in roadSegments.Values)
            {
                Vector3 toIntersection = (intersection - segment.Center).normalized;
                if (Vector3.Dot(toIntersection, direction) > 0.7f)
                {
                    flow += segment.CurrentVehicles;
                }
            }
            return flow;
        }

        private void CoordinateGreenWave()
        {
            // 绿波协调：让相邻信号灯同步，车辆可以一路绿灯
            float waveSpeed = 40f; // km/h，绿波设计速度

            for (int i = 0; i < managedLights.Count - 1; i++)
            {
                var current = managedLights[i];
                var next = managedLights[i + 1];

                float distance = Vector3.Distance(current.Position, next.Position);
                float offset = distance / (waveSpeed / 3.6f); // 秒

                next.SetPhaseOffset(offset);
            }
        }

        private float CalculateBusDelay(BusRoute route)
        {
            // 计算公交延误
            float expectedTime = route.TotalStops * 2f; // 每站 2 分钟
            float actualTime = expectedTime * (1f + congestionLevel);
            return actualTime - expectedTime;
        }

        private void PrioritizeBusAtIntersections(BusRoute route)
        {
            // 在公交经过的交叉口给予信号优先
            foreach (var stop in route.Stops)
            {
                var nearbyLights = managedLights
                    .Where(l => Vector3.Distance(l.Position, stop.Position) < 30f);

                foreach (var light in nearbyLights)
                {
                    light.RequestPriority("bus", 10f); // 公交优先 10 秒
                }
            }
        }

        private List<RoadSegment> GetBoundarySegments(TrafficAgent neighbor)
        {
            // 获取与邻居交界的路段
            return roadSegments.Values
                .Where(s => Vector3.Distance(s.Center, neighbor.transform.position) < zoneRadius * 1.5f)
                .ToList();
        }

        /// <summary>
        /// 接收邻居共享的交通信息
        /// </summary>
        public void ReceiveSharedTrafficInfo(string segmentId, float congestion)
        {
            if (roadSegments.ContainsKey(segmentId))
            {
                roadSegments[segmentId].NeighborCongestion = congestion;
            }
        }

        // ========== 事件处理 ==========

        private void OnVehicleSpawned(EventMessage msg)
        {
            // 新车辆进入区域
        }

        private void OnVehicleDespawned(EventMessage msg)
        {
            // 车辆离开区域
        }

        private void OnAccidentReported(EventMessage msg)
        {
            accidentCount++;
            currentState = AgentState.Planning;
        }

        private void OnRoadClosed(EventMessage msg)
        {
            // 道路封闭，需要重新规划
            if (msg.Data is RoadCloseData data)
            {
                if (roadSegments.ContainsKey(data.SegmentId))
                {
                    roadSegments[data.SegmentId].IsClosed = true;
                    RerouteTraffic(data.SegmentId);
                }
            }
        }

        private void OnTrafficRequest(EventMessage msg)
        {
            // 处理来自其他 Agent 的交通查询
        }

        private void OnCollaborationRequest(EventMessage msg)
        {
            if (msg.Data is CollaborationMessage collab && collab.ToAgentId == agentId)
            {
                // 评估是否能帮助
                if (collab.Request.RequestType == "traffic_diversion" && congestionLevel < 0.5f)
                {
                    // 接受协作，接收分流车辆
                    AcceptDiversion(collab.FromAgentId);
                }
            }
        }

        private void RerouteTraffic(string closedSegmentId)
        {
            // 通知导航系统重新规划路线
            EmitEvent("reroute_request", new
            {
                closedSegmentId,
                alternativeSegments = GetAlternativeSegments(closedSegmentId)
            });
        }

        private List<string> GetAlternativeSegments(string closedSegmentId)
        {
            return roadSegments.Keys.Where(id => id != closedSegmentId).ToList();
        }

        private void AcceptDiversion(string fromAgentId)
        {
            // 接受分流：临时增加本区域的通行能力
            foreach (var segment in roadSegments.Values)
            {
                segment.TemporaryCapacityBonus = segment.Capacity * 0.2f;
            }

            EmitEvent("diversion_accepted", new
            {
                fromZone = fromAgentId,
                toZone = zoneId
            });
        }
    }

    // ========== 数据结构 ==========

    [System.Serializable]
    public class TrafficLight
    {
        public string LightId;
        public Vector3 Position;
        public LightPhase CurrentPhase;
        public float PhaseTimer;
        public float GreenDuration = 30f;
        public float RedDuration = 30f;
        public float YellowDuration = 5f;
        public float PhaseOffset;
        public bool IsMalfunctioning;
        public Dictionary<string, float> PriorityRequests = new Dictionary<string, float>();

        public void SetGreenDuration(string direction, float duration)
        {
            GreenDuration = Mathf.Clamp(duration, 10f, 90f);
        }

        public void SetBaseCycleDuration(float duration)
        {
            GreenDuration = duration;
            RedDuration = duration;
        }

        public void SetPhaseOffset(float offset)
        {
            PhaseOffset = offset;
        }

        public void ExtendRedPhase(float seconds)
        {
            RedDuration += seconds;
        }

        public void RequestPriority(string requester, float seconds)
        {
            PriorityRequests[requester] = seconds;
        }
    }

    public enum LightPhase { Green, Yellow, Red }

    [System.Serializable]
    public class RoadSegment
    {
        public string SegmentId;
        public Vector3 Center;
        public int Capacity;
        public int CurrentVehicles;
        public float Congestion;
        public float NeighborCongestion;
        public bool IsClosed;
        public bool HasTidalLane;
        public TidalDirection TidalLaneDirection;
        public float TemporaryCapacityBonus;
    }

    public enum TidalDirection { None, Inbound, Outbound }

    [System.Serializable]
    public class BusRoute
    {
        public string RouteId;
        public List<BusStop> Stops;
        public float Frequency; // 发车间隔（分钟）
        public int ActiveBuses;
    }

    [System.Serializable]
    public class BusStop
    {
        public string StopId;
        public Vector3 Position;
        public int WaitingPassengers;
    }

    public class Vehicle : MonoBehaviour
    {
        public string CurrentSegmentId;
        public float Speed;
        public Vector3 Destination;
    }

    [System.Serializable]
    public class RoadCloseData
    {
        public string SegmentId;
        public string Reason;
        public float Duration;
    }
}

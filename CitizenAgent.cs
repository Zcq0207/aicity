// ============================================================================
// CitizenAgent.cs — 市民 Agent：独立个体，有需求、目标、行为
// ============================================================================
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using AICity.Core;

namespace AICity.Agents
{
    /// <summary>
    /// 市民 Agent — 每个市民都是独立的智能体
    /// 拥有：需求系统、日常行为、社交关系、职业、记忆
    /// </summary>
    public class CitizenAgent : AgentBase
    {
        // ========== 基础属性 ==========
        [Header("市民属性")]
        [SerializeField] private int age;
        [SerializeField] private Gender gender;
        [SerializeField] private Profession profession;
        [SerializeField] private float wealth;
        [SerializeField] private float education; // 0-1

        [Header("当前住所和工作")]
        [SerializeField] private string homeId;
        [SerializeField] private string workplaceId;

        // ========== 需求系统 ==========
        [Header("需求 (0-100)")]
        [SerializeField] private float hunger = 50f;
        [SerializeField] private float energy = 80f;
        [SerializeField] private float happiness = 60f;
        [SerializeField] private float health = 90f;
        [SerializeField] private float social = 50f;
        [SerializeField] private float safety = 70f;

        // ========== 行为状态 ==========
        private CitizenBehavior currentBehavior = CitizenBehavior.Idle;
        private Vector3 targetPosition;
        private float behaviorTimer;
        private NavMeshAgent navAgent;

        // ========== 社交关系 ==========
        private Dictionary<string, float> relationships = new Dictionary<string, float>();

        // ========== 日程系统 ==========
        private DailySchedule schedule;

        protected override void Awake()
        {
            base.Awake();
            navAgent = GetComponent<NavMeshAgent>();
            schedule = new DailySchedule();
        }

        protected override void InitializeMemory()
        {
            // 初始化市民的长期记忆
            Remember("home_position", transform.position, 1f);
            Remember("daily_routine", schedule, 0.8f);
            Remember("profession", profession, 0.9f);
        }

        protected override void SubscribeEvents()
        {
            eventBus.Subscribe("city_policy_changed", OnPolicyChanged);
            eventBus.Subscribe("emergency_alert", OnEmergency);
            eventBus.Subscribe("social_invitation", OnSocialInvitation);
        }

        protected override void UnsubscribeEvents()
        {
            eventBus.Unsubscribe("city_policy_changed", OnPolicyChanged);
            eventBus.Unsubscribe("emergency_alert", OnEmergency);
            eventBus.Unsubscribe("social_invitation", OnSocialInvitation);
        }

        // ========== 核心循环 ==========

        protected override void Perception()
        {
            // 感知周围环境
            float time = worldState.GameTime;

            // 根据时间自然衰减需求
            hunger = Mathf.Min(100f, hunger + Time.deltaTime * 0.5f);
            energy = Mathf.Max(0f, energy - Time.deltaTime * 0.3f);
            social = Mathf.Max(0f, social - Time.deltaTime * 0.1f);

            // 夜间需要更多睡眠
            if (worldState.IsNightTime())
            {
                energy = Mathf.Max(0f, energy - Time.deltaTime * 0.5f);
            }

            // 检查周围是否有危险
            var nearbyDangers = GetEntitiesInRange<DangerSource>(20f);
            safety = nearbyDangers.Count > 0 ? 20f : Mathf.Lerp(safety, 80f, Time.deltaTime);

            // 检查周围是否有其他市民（社交）
            var nearbyCitizens = GetEntitiesInRange<CitizenAgent>(10f);
            if (nearbyCitizens.Count > 0)
            {
                social = Mathf.Min(100f, social + Time.deltaTime * nearbyCitizens.Count * 0.2f);
            }

            // 将感知结果存入工作记忆
            memory.SetWorking("hunger", hunger);
            memory.SetWorking("energy", energy);
            memory.SetWorking("safety", safety);
            memory.SetWorking("nearby_citizens", nearbyCitizens.Count);
        }

        protected override void Think()
        {
            // 评估当前最紧迫的需求
            var urgentNeed = GetMostUrgentNeed();
            memory.SetWorking("urgent_need", urgentNeed);

            // 评估是否需要改变当前行为
            if (ShouldChangeBehavior(urgentNeed))
            {
                currentState = AgentState.Planning;
            }
        }

        protected override void Decide()
        {
            if (currentState != AgentState.Planning)
                return;

            var urgentNeed = (NeedType)memory.Recall<NeedType>("urgent_need");

            // 决策树：根据最紧迫需求选择行为
            switch (urgentNeed)
            {
                case NeedType.Hunger:
                    SetBehavior(CitizenBehavior.SeekingFood);
                    break;
                case NeedType.Energy:
                    if (worldState.IsNightTime())
                        SetBehavior(CitizenBehavior.Sleeping);
                    else
                        SetBehavior(CitizenBehavior.Resting);
                    break;
                case NeedType.Safety:
                    SetBehavior(CitizenBehavior.Fleeing);
                    break;
                case NeedType.Social:
                    SetBehavior(CitizenBehavior.Socializing);
                    break;
                case NeedType.Happiness:
                    SetBehavior(CitizenBehavior.Recreation);
                    break;
                default:
                    // 按日程行动
                    FollowSchedule();
                    break;
            }

            currentState = AgentState.Executing;
        }

        protected override void Act()
        {
            if (currentState != AgentState.Executing)
                return;

            switch (currentBehavior)
            {
                case CitizenBehavior.GoingToWork:
                    ActGoToWork();
                    break;
                case CitizenBehavior.Working:
                    ActWork();
                    break;
                case CitizenBehavior.GoingHome:
                    ActGoHome();
                    break;
                case CitizenBehavior.Sleeping:
                    ActSleep();
                    break;
                case CitizenBehavior.SeekingFood:
                    ActSeekFood();
                    break;
                case CitizenBehavior.Socializing:
                    ActSocialize();
                    break;
                case CitizenBehavior.Recreation:
                    ActRecreation();
                    break;
                case CitizenBehavior.Fleeing:
                    ActFlee();
                    break;
                case CitizenBehavior.Commuting:
                    ActCommute();
                    break;
            }
        }

        // ========== 行为实现 ==========

        private void ActGoToWork()
        {
            if (navAgent == null || navAgent.remainingDistance < 2f)
            {
                SetBehavior(CitizenBehavior.Working);
            }
        }

        private void ActWork()
        {
            behaviorTimer -= Time.deltaTime;
            energy = Mathf.Max(0f, energy - Time.deltaTime * 0.4f);

            // 工作产生收入
            wealth += GetSalaryPerSecond() * Time.deltaTime;

            // 工作 8 小时后下班
            if (behaviorTimer <= 0f)
            {
                EmitEvent("work_complete", new { profession, hoursWorked = 8f });
                SetBehavior(CitizenBehavior.GoingHome);
            }
        }

        private void ActGoHome()
        {
            if (navAgent == null || navAgent.remainingDistance < 2f)
            {
                SetBehavior(CitizenBehavior.Idle);
            }
        }

        private void ActSleep()
        {
            energy = Mathf.Min(100f, energy + Time.deltaTime * 2f);
            hunger = Mathf.Min(100f, hunger + Time.deltaTime * 0.1f);

            if (energy >= 95f)
            {
                SetBehavior(CitizenBehavior.Idle);
                currentState = AgentState.Planning;
            }
        }

        private void ActSeekFood()
        {
            // 寻找最近的餐饮设施
            var restaurants = worldState.GetBuildingsByType(BuildingType.Commercial);
            if (restaurants.Count > 0)
            {
                var nearest = FindNearest(restaurants);
                if (navAgent != null)
                    navAgent.SetDestination(nearest.WorldPosition);

                hunger = Mathf.Max(0f, hunger - Time.deltaTime * 3f);
                wealth -= Time.deltaTime * 0.5f; // 消费

                if (hunger <= 20f)
                {
                    SetBehavior(CitizenBehavior.Idle);
                    currentState = AgentState.Planning;
                }
            }
        }

        private void ActSocialize()
        {
            var nearbyCitizens = GetEntitiesInRange<CitizenAgent>(15f);
            if (nearbyCitizens.Count > 0)
            {
                social = Mathf.Min(100f, social + Time.deltaTime * 2f);
                happiness = Mathf.Min(100f, happiness + Time.deltaTime * 0.5f);

                // 建立社交关系
                foreach (var citizen in nearbyCitizens)
                {
                    if (!relationships.ContainsKey(citizen.AgentId))
                        relationships[citizen.AgentId] = 0f;
                    relationships[citizen.AgentId] = Mathf.Min(1f,
                        relationships[citizen.AgentId] + Time.deltaTime * 0.01f);
                }
            }

            behaviorTimer -= Time.deltaTime;
            if (behaviorTimer <= 0f || social >= 90f)
            {
                SetBehavior(CitizenBehavior.Idle);
                currentState = AgentState.Planning;
            }
        }

        private void ActRecreation()
        {
            // 去公园或娱乐设施
            var parks = worldState.GetBuildingsByType(BuildingType.Park);
            if (parks.Count > 0)
            {
                var nearest = FindNearest(parks);
                if (navAgent != null)
                    navAgent.SetDestination(nearest.WorldPosition);

                happiness = Mathf.Min(100f, happiness + Time.deltaTime * 1.5f);
                health = Mathf.Min(100f, health + Time.deltaTime * 0.3f);
            }

            behaviorTimer -= Time.deltaTime;
            if (behaviorTimer <= 0f)
            {
                SetBehavior(CitizenBehavior.Idle);
                currentState = AgentState.Planning;
            }
        }

        private void ActFlee()
        {
            // 远离危险源
            var dangers = GetEntitiesInRange<DangerSource>(30f);
            if (dangers.Count > 0)
            {
                Vector3 fleeDirection = Vector3.zero;
                foreach (var danger in dangers)
                {
                    fleeDirection += (transform.position - danger.transform.position).normalized;
                }
                fleeDirection.Normalize();

                if (navAgent != null)
                    navAgent.SetDestination(transform.position + fleeDirection * 50f);
            }

            // 报警
            EmitEvent("citizen_in_danger", new { citizenId = agentId, position = transform.position });

            if (dangers.Count == 0)
            {
                SetBehavior(CitizenBehavior.Idle);
                currentState = AgentState.Planning;
            }
        }

        private void ActCommute()
        {
            if (navAgent == null || navAgent.remainingDistance < 2f)
            {
                // 到达目的地
                var destination = memory.Recall<string>("commute_destination");
                if (destination == "work")
                    SetBehavior(CitizenBehavior.Working);
                else
                    SetBehavior(CitizenBehavior.Idle);

                currentState = AgentState.Planning;
            }
        }

        // ========== 辅助方法 ==========

        private NeedType GetMostUrgentNeed()
        {
            float maxUrgency = 0f;
            NeedType mostUrgent = NeedType.None;

            if (hunger > 70f && hunger > maxUrgency) { maxUrgency = hunger; mostUrgent = NeedType.Hunger; }
            if (energy < 20f && (100f - energy) > maxUrgency) { maxUrgency = 100f - energy; mostUrgent = NeedType.Energy; }
            if (safety < 30f && (100f - safety) > maxUrgency) { maxUrgency = 100f - safety; mostUrgent = NeedType.Safety; }
            if (social < 25f && (100f - social) > maxUrgency) { maxUrgency = 100f - social; mostUrgent = NeedType.Social; }
            if (happiness < 30f && (100f - happiness) > maxUrgency) { maxUrgency = 100f - happiness; mostUrgent = NeedType.Happiness; }

            return mostUrgent;
        }

        private bool ShouldChangeBehavior(NeedType urgentNeed)
        {
            if (urgentNeed == NeedType.None) return false;
            if (currentBehavior == CitizenBehavior.Fleeing) return false; // 逃跑优先

            // 饥饿度 > 80 强制中断当前行为
            if (urgentNeed == NeedType.Hunger && hunger > 80f) return true;
            if (urgentNeed == NeedType.Energy && energy < 15f) return true;
            if (urgentNeed == NeedType.Safety && safety < 20f) return true;

            return currentState == AgentState.Idle;
        }

        private void SetBehavior(CitizenBehavior behavior)
        {
            currentBehavior = behavior;
            behaviorTimer = GetBehaviorDuration(behavior);

            // 如果需要移动，设置导航目标
            switch (behavior)
            {
                case CitizenBehavior.GoingToWork:
                    NavigateTo(workplaceId);
                    break;
                case CitizenBehavior.GoingHome:
                    NavigateTo(homeId);
                    break;
            }
        }

        private float GetBehaviorDuration(CitizenBehavior behavior)
        {
            switch (behavior)
            {
                case CitizenBehavior.Working: return 8f * 60f; // 8 小时（游戏时间）
                case CitizenBehavior.Sleeping: return 7f * 60f;
                case CitizenBehavior.Socializing: return 2f * 60f;
                case CitizenBehavior.Recreation: return 3f * 60f;
                default: return 0f;
            }
        }

        private void NavigateTo(string buildingId)
        {
            // 通过建筑 ID 导航（简化实现）
            if (navAgent != null)
            {
                // 实际项目中需要从建筑系统获取位置
                currentState = AgentState.Executing;
            }
        }

        private void FollowSchedule()
        {
            float hour = worldState.GameTime;
            if (hour >= 7f && hour < 8f) SetBehavior(CitizenBehavior.SeekingFood);
            else if (hour >= 8f && hour < 9f) SetBehavior(CitizenBehavior.Commuting);
            else if (hour >= 9f && hour < 17f) SetBehavior(CitizenBehavior.GoingToWork);
            else if (hour >= 17f && hour < 18f) SetBehavior(CitizenBehavior.Commuting);
            else if (hour >= 18f && hour < 20f) SetBehavior(CitizenBehavior.Recreation);
            else if (hour >= 20f && hour < 22f) SetBehavior(CitizenBehavior.Socializing);
            else SetBehavior(CitizenBehavior.GoingHome);
        }

        private BuildingData FindNearest(List<BuildingData> buildings)
        {
            BuildingData nearest = null;
            float minDist = float.MaxValue;
            foreach (var b in buildings)
            {
                float dist = Vector3.Distance(transform.position, b.WorldPosition);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = b;
                }
            }
            return nearest;
        }

        private float GetSalaryPerSecond()
        {
            return profession switch
            {
                Profession.Worker => 0.5f,
                Profession.Engineer => 1.2f,
                Profession.Doctor => 1.8f,
                Profession.Teacher => 0.8f,
                Profession.ShopOwner => 1.0f,
                _ => 0.3f
            };
        }

        // ========== 事件处理 ==========

        private void OnPolicyChanged(EventMessage msg)
        {
            // 政策变化影响市民行为
            if (msg.Data is PolicyData policy)
            {
                switch (policy.Type)
                {
                    case PolicyType.Curfew:
                        if (worldState.IsNightTime())
                            SetBehavior(CitizenBehavior.GoingHome);
                        break;
                    case PolicyType.TaxIncrease:
                        happiness -= 5f;
                        break;
                    case PolicyType.PublicEvent:
                        happiness += 10f;
                        break;
                }
            }
        }

        private void OnEmergency(EventMessage msg)
        {
            safety = 10f;
            SetBehavior(CitizenBehavior.Fleeing);
        }

        private void OnSocialInvitation(EventMessage msg)
        {
            if (currentBehavior == CitizenBehavior.Idle || currentBehavior == CitizenBehavior.Recreation)
            {
                SetBehavior(CitizenBehavior.Socializing);
            }
        }
    }

    // ========== 枚举 ==========

    public enum Gender { Male, Female }

    public enum Profession
    {
        Unemployed,
        Worker,
        Engineer,
        Doctor,
        Teacher,
        ShopOwner,
        Farmer,
        Police,
        Firefighter
    }

    public enum CitizenBehavior
    {
        Idle,
        GoingToWork,
        Working,
        GoingHome,
        Sleeping,
        SeekingFood,
        Socializing,
        Recreation,
        Fleeing,
        Commuting
    }

    public enum NeedType
    {
        None,
        Hunger,
        Energy,
        Safety,
        Social,
        Happiness,
        Health
    }

    // ========== 日程系统 ==========

    [System.Serializable]
    public class DailySchedule
    {
        public ScheduleEntry[] WeekdaySchedule;
        public ScheduleEntry[] WeekendSchedule;

        public DailySchedule()
        {
            WeekdaySchedule = new ScheduleEntry[]
            {
                new ScheduleEntry { Hour = 7, Behavior = CitizenBehavior.SeekingFood },
                new ScheduleEntry { Hour = 8, Behavior = CitizenBehavior.Commuting },
                new ScheduleEntry { Hour = 9, Behavior = CitizenBehavior.Working },
                new ScheduleEntry { Hour = 17, Behavior = CitizenBehavior.Commuting },
                new ScheduleEntry { Hour = 18, Behavior = CitizenBehavior.Recreation },
                new ScheduleEntry { Hour = 21, Behavior = CitizenBehavior.GoingHome },
                new ScheduleEntry { Hour = 22, Behavior = CitizenBehavior.Sleeping }
            };

            WeekendSchedule = new ScheduleEntry[]
            {
                new ScheduleEntry { Hour = 9, Behavior = CitizenBehavior.SeekingFood },
                new ScheduleEntry { Hour = 10, Behavior = CitizenBehavior.Recreation },
                new ScheduleEntry { Hour = 14, Behavior = CitizenBehavior.Socializing },
                new ScheduleEntry { Hour = 18, Behavior = CitizenBehavior.SeekingFood },
                new ScheduleEntry { Hour = 20, Behavior = CitizenBehavior.Recreation },
                new ScheduleEntry { Hour = 23, Behavior = CitizenBehavior.Sleeping }
            };
        }
    }

    [System.Serializable]
    public class ScheduleEntry
    {
        public float Hour;
        public CitizenBehavior Behavior;
    }

    // ========== 占位组件（用于感知检测） ==========
    public class DangerSource : MonoBehaviour { }
}

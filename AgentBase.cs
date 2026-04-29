// ============================================================================
// AgentBase.cs — 所有 Agent 的基类
// ============================================================================
using UnityEngine;
using System.Collections.Generic;

namespace AICity.Core
{
    /// <summary>
    /// Agent 生命周期状态
    /// </summary>
    public enum AgentState
    {
        Idle,
        Planning,
        Executing,
        Waiting,
        Completed,
        Failed
    }

    /// <summary>
    /// Agent 基类 — 所有城市中的智能体都继承此类
    /// 负责：感知 → 思考 → 决策 → 行为 的循环
    /// </summary>
    public abstract class AgentBase : MonoBehaviour
    {
        [Header("Agent 基础配置")]
        [SerializeField] protected string agentId;
        [SerializeField] protected string agentName;
        [SerializeField] protected AgentState currentState = AgentState.Idle;

        [Header("感知范围")]
        [SerializeField] protected float perceptionRadius = 50f;

        // Agent 的记忆系统（短期 + 长期）
        protected AgentMemory memory = new AgentMemory();

        // 当前正在执行的任务
        protected AgentTask currentTask;

        // 任务队列
        protected Queue<AgentTask> taskQueue = new Queue<AgentTask>();

        // 世界状态的本地缓存
        protected WorldState worldState;

        // 事件总线引用
        protected EventBus eventBus;

        // ========== 属性 ==========
        public string AgentId => agentId;
        public string AgentName => agentName;
        public AgentState CurrentState => currentState;
        public AgentMemory Memory => memory;

        // ========== 生命周期 ==========

        protected virtual void Awake()
        {
            if (string.IsNullOrEmpty(agentId))
                agentId = System.Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        protected virtual void Start()
        {
            eventBus = EventBus.Instance;
            worldState = WorldState.Instance;

            // 注册到 Agent 管理器
            AgentManager.Instance?.RegisterAgent(this);

            // 订阅相关事件
            SubscribeEvents();

            // 初始化记忆
            InitializeMemory();
        }

        protected virtual void OnDestroy()
        {
            UnsubscribeEvents();
            AgentManager.Instance?.UnregisterAgent(this);
        }

        /// <summary>
        /// 核心更新循环：感知 → 思考 → 决策 → 行为
        /// </summary>
        protected virtual void Update()
        {
            if (currentState == AgentState.Failed || currentState == AgentState.Completed)
                return;

            // 1. 感知：收集周围环境信息
            Perception();

            // 2. 思考：评估当前状态和目标
            Think();

            // 3. 决策：选择行为
            Decide();

            // 4. 执行：执行当前任务
            Act();
        }

        // ========== 核心抽象方法 ==========

        /// <summary>
        /// 感知阶段 — 收集环境信息，更新内部状态
        /// </summary>
        protected abstract void Perception();

        /// <summary>
        /// 思考阶段 — 评估当前状态，分析目标
        /// </summary>
        protected abstract void Think();

        /// <summary>
        /// 决策阶段 — 选择下一个行为
        /// </summary>
        protected abstract void Decide();

        /// <summary>
        /// 行为阶段 — 执行当前任务
        /// </summary>
        protected abstract void Act();

        /// <summary>
        /// 初始化 Agent 的长期记忆
        /// </summary>
        protected abstract void InitializeMemory();

        /// <summary>
        /// 订阅事件总线的事件
        /// </summary>
        protected abstract void SubscribeEvents();

        /// <summary>
        /// 取消订阅事件
        /// </summary>
        protected abstract void UnsubscribeEvents();

        // ========== 通用方法 ==========

        /// <summary>
        /// 向任务队列添加新任务
        /// </summary>
        public void AddTask(AgentTask task)
        {
            taskQueue.Enqueue(task);
        }

        /// <summary>
        /// 获取 Agent 的感知范围内的实体
        /// </summary>
        protected List<T> GetEntitiesInRange<T>(float range) where T : Component
        {
            var results = new List<T>();
            Collider[] colliders = Physics.OverlapSphere(transform.position, range);
            foreach (var col in colliders)
            {
                var entity = col.GetComponent<T>();
                if (entity != null && entity.gameObject != gameObject)
                    results.Add(entity);
            }
            return results;
        }

        /// <summary>
        /// 向记忆系统写入事件
        /// </summary>
        protected void Remember(string key, object value, float importance = 0.5f)
        {
            memory.Store(key, value, importance);
        }

        /// <summary>
        /// 从记忆系统回忆
        /// </summary>
        protected T Recall<T>(string key)
        {
            return memory.Recall<T>(key);
        }

        /// <summary>
        /// 广播 Agent 自身事件
        /// </summary>
        protected void EmitEvent(string eventName, object data = null)
        {
            eventBus?.Publish(agentId, eventName, data);
        }

        /// <summary>
        /// 请求其他 Agent 协作
        /// </summary>
        protected void RequestCollaboration(string targetAgentId, CollaborationRequest request)
        {
            eventBus?.Publish(agentId, "collaboration_request", new CollaborationMessage
            {
                FromAgentId = agentId,
                ToAgentId = targetAgentId,
                Request = request
            });
        }
    }

    // ========== 数据结构 ==========

    /// <summary>
    /// Agent 任务
    /// </summary>
    [System.Serializable]
    public class AgentTask
    {
        public string TaskId;
        public string TaskType;
        public string Description;
        public Dictionary<string, object> Parameters;
        public float Priority;
        public float Deadline;
        public System.Action OnComplete;
        public System.Action<string> OnFail;

        public AgentTask()
        {
            TaskId = System.Guid.NewGuid().ToString("N").Substring(0, 8);
            Parameters = new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// Agent 记忆系统
    /// </summary>
    public class AgentMemory
    {
        // 短期记忆（当前帧/当前任务相关）
        private Dictionary<string, MemoryEntry> shortTerm = new Dictionary<string, MemoryEntry>();

        // 长期记忆（重要事件、学习到的模式）
        private Dictionary<string, MemoryEntry> longTerm = new Dictionary<string, MemoryEntry>();

        // 工作记忆（当前正在处理的信息）
        private Dictionary<string, object> workingMemory = new Dictionary<string, object>();

        public void Store(string key, object value, float importance = 0.5f)
        {
            var entry = new MemoryEntry
            {
                Key = key,
                Value = value,
                Importance = importance,
                Timestamp = Time.time,
                AccessCount = 0
            };

            if (importance > 0.7f)
                longTerm[key] = entry;
            else
                shortTerm[key] = entry;
        }

        public T Recall<T>(string key)
        {
            if (workingMemory.TryGetValue(key, out var wm))
                return (T)wm;

            if (shortTerm.TryGetValue(key, out var st))
            {
                st.AccessCount++;
                return (T)st.Value;
            }

            if (longTerm.TryGetValue(key, out var lt))
            {
                lt.AccessCount++;
                return (T)lt.Value;
            }

            return default;
        }

        public bool HasMemory(string key)
        {
            return workingMemory.ContainsKey(key) ||
                   shortTerm.ContainsKey(key) ||
                   longTerm.ContainsKey(key);
        }

        /// <summary>
        /// 清理过期的短期记忆
        /// </summary>
        public void CleanupShortTerm(float maxAge = 60f)
        {
            var keysToRemove = new List<string>();
            foreach (var kvp in shortTerm)
            {
                if (Time.time - kvp.Value.Timestamp > maxAge)
                    keysToRemove.Add(kvp.Key);
            }
            foreach (var key in keysToRemove)
                shortTerm.Remove(key);
        }

        public void SetWorking(string key, object value)
        {
            workingMemory[key] = value;
        }

        public void ClearWorking()
        {
            workingMemory.Clear();
        }
    }

    [System.Serializable]
    public class MemoryEntry
    {
        public string Key;
        public object Value;
        public float Importance;
        public float Timestamp;
        public int AccessCount;
    }

    /// <summary>
    /// 协作请求
    /// </summary>
    [System.Serializable]
    public class CollaborationRequest
    {
        public string RequestType;
        public string Description;
        public Dictionary<string, object> Data;
        public float Urgency; // 0-1
    }

    [System.Serializable]
    public class CollaborationMessage
    {
        public string FromAgentId;
        public string ToAgentId;
        public CollaborationRequest Request;
    }
}

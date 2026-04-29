// ============================================================================
// AgentManager.cs — Agent 注册、查询、全局调度
// ============================================================================
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace AICity.Core
{
    /// <summary>
    /// Agent 管理器 — 维护所有 Agent 的注册表，提供全局查询和调度
    /// </summary>
    public class AgentManager : MonoBehaviour
    {
        public static AgentManager Instance { get; private set; }

        // 所有注册的 Agent
        private Dictionary<string, AgentBase> agents = new Dictionary<string, AgentBase>();

        // 按类型索引
        private Dictionary<System.Type, List<AgentBase>> agentsByType
            = new Dictionary<System.Type, List<AgentBase>>();

        // Agent 总数统计
        public int AgentCount => agents.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// 注册 Agent
        /// </summary>
        public void RegisterAgent(AgentBase agent)
        {
            if (agents.ContainsKey(agent.AgentId))
            {
                Debug.LogWarning($"[AgentManager] Agent {agent.AgentId} 已注册，跳过");
                return;
            }

            agents[agent.AgentId] = agent;

            var type = agent.GetType();
            if (!agentsByType.ContainsKey(type))
                agentsByType[type] = new List<AgentBase>();
            agentsByType[type].Add(agent);

            Debug.Log($"[AgentManager] 注册 Agent: {agent.AgentName} ({agent.AgentId})");
        }

        /// <summary>
        /// 注销 Agent
        /// </summary>
        public void UnregisterAgent(AgentBase agent)
        {
            agents.Remove(agent.AgentId);

            var type = agent.GetType();
            if (agentsByType.ContainsKey(type))
                agentsByType[type].Remove(agent);
        }

        /// <summary>
        /// 根据 ID 获取 Agent
        /// </summary>
        public AgentBase GetAgent(string agentId)
        {
            agents.TryGetValue(agentId, out var agent);
            return agent;
        }

        /// <summary>
        /// 获取指定类型的所有 Agent
        /// </summary>
        public List<T> GetAgentsOfType<T>() where T : AgentBase
        {
            var type = typeof(T);
            if (agentsByType.TryGetValue(type, out var list))
                return list.Cast<T>().ToList();
            return new List<T>();
        }

        /// <summary>
        /// 获取指定位置附近的 Agent
        /// </summary>
        public List<AgentBase> GetAgentsInRange(Vector3 position, float range)
        {
            return agents.Values
                .Where(a => Vector3.Distance(a.transform.position, position) <= range)
                .ToList();
        }

        /// <summary>
        /// 获取所有 Agent 的状态摘要（用于 UI 显示）
        /// </summary>
        public Dictionary<string, AgentStatus> GetAllStatus()
        {
            var status = new Dictionary<string, AgentStatus>();
            foreach (var kvp in agents)
            {
                status[kvp.Key] = new AgentStatus
                {
                    AgentId = kvp.Key,
                    AgentName = kvp.Value.AgentName,
                    State = kvp.Value.CurrentState,
                    Position = kvp.Value.transform.position
                };
            }
            return status;
        }
    }

    [System.Serializable]
    public class AgentStatus
    {
        public string AgentId;
        public string AgentName;
        public AgentState State;
        public Vector3 Position;
    }
}

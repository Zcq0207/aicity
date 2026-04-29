// ============================================================================
// EventBus.cs — 全局事件总线，Agent 间通信的核心
// ============================================================================
using UnityEngine;
using System;
using System.Collections.Generic;

namespace AICity.Core
{
    /// <summary>
    /// 事件总线 — 实现 Agent 间的松耦合通信
    /// 支持：发布/订阅、点对点消息、广播
    /// </summary>
    public class EventBus : MonoBehaviour
    {
        public static EventBus Instance { get; private set; }

        // 事件订阅表：eventName → List<callback>
        private Dictionary<string, List<Action<EventMessage>>> subscribers
            = new Dictionary<string, List<Action<EventMessage>>>();

        // Agent 专属消息队列：agentId → Queue<message>
        private Dictionary<string, Queue<EventMessage>> agentMailboxes
            = new Dictionary<string, Queue<EventMessage>>();

        // 事件日志（用于调试和回放）
        private List<EventLog> eventLog = new List<EventLog>();
        private const int MAX_LOG_SIZE = 10000;

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

        // ========== 发布/订阅 ==========

        /// <summary>
        /// 订阅事件
        /// </summary>
        public void Subscribe(string eventName, Action<EventMessage> callback)
        {
            if (!subscribers.ContainsKey(eventName))
                subscribers[eventName] = new List<Action<EventMessage>>();
            subscribers[eventName].Add(callback);
        }

        /// <summary>
        /// 取消订阅
        /// </summary>
        public void Unsubscribe(string eventName, Action<EventMessage> callback)
        {
            if (subscribers.ContainsKey(eventName))
                subscribers[eventName].Remove(callback);
        }

        /// <summary>
        /// 发布事件（广播给所有订阅者）
        /// </summary>
        public void Publish(string senderId, string eventName, object data = null)
        {
            var message = new EventMessage
            {
                SenderId = senderId,
                EventName = eventName,
                Data = data,
                Timestamp = Time.time
            };

            // 记录日志
            LogEvent(message);

            // 通知所有订阅者
            if (subscribers.TryGetValue(eventName, out var callbacks))
            {
                foreach (var callback in callbacks.ToArray()) // ToArray 防止迭代中修改
                {
                    try
                    {
                        callback.Invoke(message);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[EventBus] 处理事件 {eventName} 时出错: {e.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 发送点对点消息到 Agent 邮箱
        /// </summary>
        public void SendTo(string senderId, string targetAgentId, string eventName, object data = null)
        {
            var message = new EventMessage
            {
                SenderId = senderId,
                TargetId = targetAgentId,
                EventName = eventName,
                Data = data,
                Timestamp = Time.time
            };

            if (!agentMailboxes.ContainsKey(targetAgentId))
                agentMailboxes[targetAgentId] = new Queue<EventMessage>();

            agentMailboxes[targetAgentId].Enqueue(message);
            LogEvent(message);
        }

        /// <summary>
        /// 读取 Agent 邮箱中的消息
        /// </summary>
        public EventMessage ReadMail(string agentId)
        {
            if (agentMailboxes.TryGetValue(agentId, out var mailbox) && mailbox.Count > 0)
                return mailbox.Dequeue();
            return null;
        }

        /// <summary>
        /// 检查 Agent 是否有未读消息
        /// </summary>
        public bool HasMail(string agentId)
        {
            return agentMailboxes.TryGetValue(agentId, out var mailbox) && mailbox.Count > 0;
        }

        // ========== 日志 ==========

        private void LogEvent(EventMessage message)
        {
            eventLog.Add(new EventLog
            {
                Message = message,
                Frame = Time.frameCount
            });

            if (eventLog.Count > MAX_LOG_SIZE)
                eventLog.RemoveAt(0);
        }

        public List<EventLog> GetRecentLogs(int count = 100)
        {
            int start = Mathf.Max(0, eventLog.Count - count);
            return eventLog.GetRange(start, eventLog.Count - start);
        }
    }

    // ========== 数据结构 ==========

    [System.Serializable]
    public class EventMessage
    {
        public string SenderId;
        public string TargetId;
        public string EventName;
        public object Data;
        public float Timestamp;
    }

    [System.Serializable]
    public class EventLog
    {
        public EventMessage Message;
        public int Frame;
    }
}

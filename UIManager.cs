// ============================================================================
// UIManager.cs — 城市仪表盘 + 自然语言输入面板
// ============================================================================
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using AICity.Core;
using AICity.Agents;

namespace AICity.UI
{
    /// <summary>
    /// UI 管理器 — 显示城市状态、Agent 活动、政策输入
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [Header("城市统计面板")]
        [SerializeField] private Text populationText;
        [SerializeField] private Text budgetText;
        [SerializeField] private Text happinessText;
        [SerializeField] private Text safetyText;
        [SerializeField] private Text pollutionText;
        [SerializeField] private Text timeText;
        [SerializeField] private Text dayText;

        [Header("经济面板")]
        [SerializeField] private Text unemploymentText;
        [SerializeField] private Text taxRateText;
        [SerializeField] private Text approvalText;

        [Header("交通面板")]
        [SerializeField] private Text congestionText;
        [SerializeField] private Text avgSpeedText;
        [SerializeField] private Text vehicleText;

        [Header("Agent 状态面板")]
        [SerializeField] private Text agentCountText;
        [SerializeField] private Transform agentListContent;
        [SerializeField] private GameObject agentStatusPrefab;

        [Header("自然语言输入")]
        [SerializeField] private InputField commandInput;
        [SerializeField] private Button sendButton;
        [SerializeField] private Text responseText;
        [SerializeField] private ScrollRect responseScrollRect;

        [Header("事件日志")]
        [SerializeField] private Transform logContent;
        [SerializeField] private GameObject logEntryPrefab;
        [SerializeField] private int maxLogEntries = 50;

        // 引用
        private EventBus eventBus;
        private WorldState worldState;
        private AgentManager agentManager;
        private PolicyAgent policyAgent;

        // 日志队列
        private Queue<string> eventLogs = new Queue<string>();

        private void Start()
        {
            eventBus = EventBus.Instance;
            worldState = WorldState.Instance;
            agentManager = AgentManager.Instance;

            // 查找政策 Agent
            var policyAgents = agentManager.GetAgentsOfType<PolicyAgent>();
            if (policyAgents.Count > 0)
                policyAgent = policyAgents[0];

            // 绑定按钮事件
            if (sendButton != null)
                sendButton.onClick.AddListener(OnSendCommand);

            if (commandInput != null)
                commandInput.onEndEdit.AddListener(OnInputEndEdit);

            // 订阅事件
            eventBus.Subscribe("city_policy_changed", OnPolicyChanged);
            eventBus.Subscribe("policy_evaluation", OnPolicyEvaluation);
            eventBus.Subscribe("policy_rejected", OnPolicyRejected);
            eventBus.Subscribe("emergency_alert", OnEmergencyAlert);
            eventBus.Subscribe("economic_report", OnEconomicReport);
        }

        private void Update()
        {
            UpdateCityStats();
            UpdateEconomicPanel();
            UpdateTrafficPanel();
            UpdateAgentPanel();
        }

        // ========== 面板更新 ==========

        private void UpdateCityStats()
        {
            if (worldState == null) return;

            SetText(populationText, $"人口: {worldState.TotalPopulation:N0}");
            SetText(budgetText, $"预算: ¥{worldState.CityBudget:N0}");
            SetText(happinessText, $"幸福度: {worldState.CityHappiness:F1}");
            SetText(safetyText, $"安全度: {worldState.CitySafety:F1}");
            SetText(pollutionText, $"污染度: {worldState.CityPollution:F1}");

            // 时间显示
            int hour = Mathf.FloorToInt(worldState.GameTime);
            int minute = Mathf.FloorToInt((worldState.GameTime - hour) * 60f);
            SetText(timeText, $"时间: {hour:D2}:{minute:D2}");
            SetText(dayText, $"第 {worldState.CurrentWeek} 周 {worldState.CurrentDay}");
        }

        private void UpdateEconomicPanel()
        {
            if (worldState == null) return;

            SetText(unemploymentText, $"失业率: {worldState.UnemploymentRate:P1}");
            SetText(taxRateText, $"税率: {worldState.TaxRate:P0}");

            // 支持率从 PolicyAgent 获取
            if (policyAgent != null)
            {
                float approval = policyAgent.Memory.Recall<float>("approval_rate");
                SetText(approvalText, $"支持率: {approval:F1}%");
            }
        }

        private void UpdateTrafficPanel()
        {
            // 从交通 Agent 获取数据
            var trafficAgents = agentManager.GetAgentsOfType<TrafficAgent>();
            if (trafficAgents.Count > 0)
            {
                var agent = trafficAgents[0];
                float congestion = agent.Memory.Recall<float>("congestion");
                float speed = agent.Memory.Recall<float>("average_speed");
                int vehicles = agent.Memory.Recall<int>("vehicle_count");

                SetText(congestionText, $"拥堵度: {congestion:P0}");
                SetText(avgSpeedText, $"平均车速: {speed:F0} km/h");
                SetText(vehicleText, $"车辆数: {vehicles}");
            }
        }

        private void UpdateAgentPanel()
        {
            if (agentManager == null) return;

            SetText(agentCountText, $"Agent 总数: {agentManager.AgentCount}");
        }

        // ========== 自然语言输入 ==========

        private void OnSendCommand()
        {
            if (commandInput == null || string.IsNullOrEmpty(commandInput.text))
                return;

            string command = commandInput.text.Trim();
            commandInput.text = "";

            // 发送到政策 Agent 处理
            eventBus.Publish("player", "natural_language_command", command);

            // 显示玩家指令
            AppendResponse($"<color=#4CAF50>玩家:</color> {command}");
        }

        private void OnInputEndEdit(string text)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                OnSendCommand();
            }
        }

        // ========== 事件处理 ==========

        private void OnPolicyChanged(EventMessage msg)
        {
            if (msg.Data is PolicyData policy)
            {
                AppendResponse($"<color=#2196F3>系统:</color> 政策已实施 - {policy.Name}");
                AddLogEntry($"[政策] {policy.Name}: {policy.Description}");
            }
        }

        private void OnPolicyEvaluation(EventMessage msg)
        {
            AppendResponse($"<color=#FF9800>评估:</color> {msg.Data}");
        }

        private void OnPolicyRejected(EventMessage msg)
        {
            AppendResponse($"<color=#F44336>拒绝:</color> {msg.Data}");
        }

        private void OnEmergencyAlert(EventMessage msg)
        {
            AppendResponse($"<color=#FF0000>紧急:</color> {msg.Data}");
            AddLogEntry($"[紧急] {msg.Data}");
        }

        private void OnEconomicReport(EventMessage msg)
        {
            AddLogEntry($"[经济报告] {msg.Data}");
        }

        // ========== UI 工具方法 ==========

        private void SetText(Text textComponent, string value)
        {
            if (textComponent != null)
                textComponent.text = value;
        }

        private void AppendResponse(string text)
        {
            if (responseText != null)
            {
                responseText.text += $"\n{text}";

                // 自动滚动到底部
                if (responseScrollRect != null)
                {
                    Canvas.ForceUpdateCanvases();
                    responseScrollRect.verticalNormalizedPosition = 0f;
                }
            }
        }

        private void AddLogEntry(string entry)
        {
            eventLogs.Enqueue(entry);
            if (eventLogs.Count > maxLogEntries)
                eventLogs.Dequeue();

            // 更新日志 UI
            if (logContent != null && logEntryPrefab != null)
            {
                // 清理旧条目
                foreach (Transform child in logContent)
                    Destroy(child.gameObject);

                // 创建新条目
                foreach (var log in eventLogs)
                {
                    var entryObj = Instantiate(logEntryPrefab, logContent);
                    var text = entryObj.GetComponentInChildren<Text>();
                    if (text != null)
                        text.text = log;
                }
            }
        }
    }
}

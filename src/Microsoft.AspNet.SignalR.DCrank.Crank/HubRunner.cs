﻿using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Client;
using Newtonsoft.Json;

namespace Microsoft.AspNet.SignalR.DCrank.Crank
{
    public class HubRunner : IRunner
    {
        private const string _hubName = "ControllerHub";

        private readonly Agent _agent;
        private readonly string _controllerUrl;
        private HubConnection _connection;
        private IHubProxy _proxy;

        public HubRunner(Agent agent, string controllerUrl)
        {
            _agent = agent;
            _controllerUrl = controllerUrl;

            _agent.Runner = this;
        }

        public async Task Run()
        {
            while (true)
            {
                try
                {
                    using (_connection = new HubConnection(_controllerUrl))
                    {
                        _proxy = _connection.CreateHubProxy(_hubName);
                        InitializeProxy();

                        Trace.WriteLine("Attempting to connect to TestController");

                        try
                        {
                            await _connection.Start();

                            await LogAgent("Agent connected to TestController.", _connection.ConnectionId);

                            while (_connection.State == ConnectionState.Connected)
                            {
                                await InvokeController("agentHeartbeat", _agent.GetHeartbeatInformation());
                                await Task.Delay(1000);
                            }
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine(string.Format("Agent failed to connect to server: {0}", ex.Message));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(string.Format("Connection lost: {0}", ex.Message));
                }

                await Task.Delay(10000);
            }
        }

        private void InitializeProxy()
        {
            _proxy.On<int>("pingAgent", value =>
            {
                LogAgent("Agent received pingAgent with value {0}.", value);
                InvokeController("pongAgent", value);
            });

            _proxy.On<string, int, int>("startWorkers", (targetAddress, numberOfWorkers, numberOfConnections) =>
            {
                LogAgent("Agent received startWorker command for target {0} with {1} workers and {2} connections.", targetAddress, numberOfWorkers, numberOfConnections);

                if (numberOfWorkers > 0 && numberOfConnections > 0)
                {
                    _agent.StartWorkers(targetAddress, numberOfWorkers, numberOfConnections);
                }
            });

            _proxy.On<int>("killWorker", workerId =>
            {
                LogAgent("Agent received killWorker command for Worker {0}.", workerId);

                _agent.KillWorker(workerId);
            });

            _proxy.On<int>("killWorkers", numberOfWorkersToKill =>
            {
                LogAgent("Agent received killWorker command to kill {0} workers.", numberOfWorkersToKill);
                _agent.KillWorkers(numberOfWorkersToKill);
            });

            _proxy.On("killConnections", () =>
            {
                LogAgent("Agent received killConnections command.");
                _agent.KillConnections();
            });

            _proxy.On<int, int>("pingWorker", (workerId, value) =>
            {
                LogAgent("Agent received pingWorker for Worker {0} with value {1}.", workerId, value);
                _agent.PingWorker(workerId, value);
            });

            _proxy.On<int, double>("startTest", (messageSize, sendIntervalSeconds) =>
            {
                LogAgent("Agent received test information with message size: {0}, and send interval: {1}.", messageSize, sendIntervalSeconds);
                _agent.StartTest(messageSize, (int)(1000 * sendIntervalSeconds));
            });

            _proxy.On<int>("stopWorker", workerId =>
            {
                LogAgent("Agent received stopWorker command for Worker {0}.", workerId);
                _agent.StopWorker(workerId);
            });

            _proxy.On("stopWorkers", () =>
            {
                LogAgent("Agent received stopWorkers command.");
                _agent.StopWorkers();
            });
        }

        public async Task PongWorker(int workerId, int value)
        {
            InvokeController("pongWorker", workerId, value);
        }

        public async Task LogWorker(int workerId, string format, params object[] arguments)
        {
            var prefix = string.Format("({0}, {1}) ", _connection.ConnectionId, workerId);
            var message = "[" + DateTime.Now.ToString() + "] " + string.Format(format, arguments);
            Trace.WriteLine(prefix + message);

            try
            {
                await _proxy.Invoke("LogWorker", workerId, message);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(prefix + string.Format("LogWorker threw an exception: {0}", ex.Message));
            }
        }

        public async Task LogAgent(string format, params object[] arguments)
        {
            var prefix = string.Format("({0}) ", _connection.ConnectionId, DateTime.Now);
            var message = "[" + DateTime.Now.ToString() + "] " + string.Format(format, arguments);
            Trace.WriteLine(prefix + message);

            try
            {
                await _proxy.Invoke("LogAgent", message);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(prefix + string.Format("LogAgent threw an exception: {0}", ex.Message));
            }
        }

        private async Task InvokeController(string command, params object[] arguments)
        {
            var commandString = command + "(" + string.Join(", ", JsonConvert.SerializeObject(arguments)) + ")";

            try
            {
                await _proxy.Invoke(command, arguments);
                LogAgent("Agent completed call to TestController: {0}", commandString);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(string.Format("Agent attempted call to TestController: {0}. Exception: {1}", command, ex.Message));
            }
        }
    }
}

// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Substrate.ContentPipeline.Publisher
{
    class Program
    {
        private static AppServiceBase m_appServiceBase;
        private static CancellationTokenSource m_cancellationTokenSource;

        static async Task Main(string[] args)
        {
            Console.WriteLine("Substrate Pipeline Publisher starting");

            // Handle exit event
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            m_cancellationTokenSource = new CancellationTokenSource();

            // Load DI infra
            m_appServiceBase = new AppServiceBase(args);
            Console.WriteLine("Substrate Pipeline Publisher started");

            await m_appServiceBase.RunMainLoopAsync(
                m_cancellationTokenSource.Token);
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            // Signal exit
            m_cancellationTokenSource.Cancel();
        }
    }
}
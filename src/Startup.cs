﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using splunk_eventhubs.Data;
using splunk_eventhubs.Hubs;
using splunk_eventhubs.Processor;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace splunk_eventhubs
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpClient();
            services.AddSignalR();
            services.AddSingleton<ConsumersRepository>();
            services.AddHostedService<LogProcessorService>();
            services.AddHttpClient<LogEventProcessor>();
            services.AddLogging();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.UseSignalR(r => r.MapHub<StatusHub>("/eh-status"));
            app.Map("/status", (b) =>
             {
                 app.Run(async context =>
                 {
                     context.Response.StatusCode = 200;
                     await context.Response.WriteAsync("{ \"status\": \"OK\"}");
                 });
             });
        }
    }
}

# Redis Rebus Error Tracker

This repository provides an implementation of Rebus error tracker using Redis, that replaces automatically the built-in InMemErrorTracker 

[![QA Build](https://github.com/pfab-io/RebusRedisErrorTracker/actions/workflows/dotnet.yml/badge.svg)](https://github.com/pfab-io/RebusRedisErrorTracker/actions/workflows/dotnet.yml)

## Setup

Install Nuget package [![NuGet](https://buildstats.info/nuget/PFabIO.Rebus.Retry.ErrorTracking.Redis)](https://www.nuget.org/packages/PFabIO.Rebus.Retry.ErrorTracking.Redis/ "Download PFabIO.Rebus.Retry.ErrorTracking.Redis from NuGet.org")


   ```c#
   var connectionMultiplexer = ConnectionMultiplexer.Connect("...");
   
   serviceCollection.AddRebus((configurer, provider) 
      => configurer
         .Transport( .. )
         .Options(x => 
            x.DecorateRedisErrorTracker(connectionMultiplexer, queueName: inputQueueName))
   });

# Redis Rebus Error Tracker

This repository provides an implementation of an error tracker for Rebus using Redis that replaces automatically the built-in InMemErrorTracker 


## Setup

   ```c#
   var connectionMultiplexer = ConnectionMultiplexer.Connect("...");
   
   serviceCollection.AddRebus((configurer, provider) 
      => configurer
         .Transport( .. )
         .Options(x => 
            x.DecorateRedisErrorTracker(connectionMultiplexer, queueName: inputQueueName))
   });

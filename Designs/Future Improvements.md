### Plan Improvements

**1. Replace the F1 IoT Hub tier immediately — even in dev**

The F1 free tier supports only 8,000 messages at 0.5KB chunk size per day. That sounds like a lot until you do the math: one sensor reading every 10 seconds across 5 sensors is 43,200 messages per day — you'd hit the wall in under 2 hours. Worse, when the quota is exceeded, the `client.send_message()` call simply hangs, which means your C++ edge agent freezes entirely with no graceful recovery unless you build explicit quota exception handling from day one. The fix is simple — an S1 hub costs about $1/day and supports 400,000 messages — but building against F1 and then migrating is a trap. Start on S1 and batch your messages aggressively (multiple sensor readings per MQTT payload up to the 256KB message limit). [Microsoft Azure + 2](https://www.azure.cn/en-us/pricing/details/iot-hub/)

**2. Rethink the Container App minimum replicas setting**

The scaffold currently doesn't address this, but it's a meaningful cost decision. A single 0.5 vCPU / 1 GiB replica running all month at idle costs approximately $13/month before any traffic charges, because idle compute isn't covered by the free tier. If you set `min-replicas=0`, the app scales to zero between messages and you stay within free tier limits during development — but you get cold starts (5–15 second delays) when IoT Hub triggers it. For a portfolio project this is fine. For a paying customer getting real-time alerts, it isn't. Build with `min-replicas=0` now and document the tradeoff clearly in your scaffold for when the first paying customer arrives. [Microsoft Azure](https://azure.microsoft.com/en-us/pricing/details/signalr-service/)

**3. The SignalR free tier is more limiting than the scaffold acknowledges**

The SignalR free plan only allows 20 concurrent connections — that's 20 browser tabs connected simultaneously, not 20 users total. For a demo with multiple people watching, or a property manager with a dashboard open on two screens, you'll hit this faster than expected. The Standard tier costs roughly $40–50/month per unit. This isn't a blocker for portfolio work, but the scaffold should note this explicitly so you don't discover it during a job interview demo. [Microsoft Azure](https://azure.microsoft.com/en-us/products/container-apps)

**4. Add BACnet as a planned v1.5 milestone, not v2**

The research confirmed that the open-source bacnet-stack library supports BACnet application layer, network layer, and MAC layer communications on Linux — and crucially, it's written in C, which C++ can link to directly via CMake. There's also a commercial plug-and-play BACnet-to-Azure IoT Hub appliance from ioTium that proves the integration path is real and commercially viable, not theoretical. The improvement here is architectural: rather than deferring BACnet entirely to v2, add a BACnet abstraction layer to the C++ edge agent in v1.5 that can be swapped in when you have a real building to test against. This is a thin interface — `ISensorReader` with a `SimulatedSensorReader` in v1 and a `BACnetSensorReader` in v1.5 — but it signals production-readiness in interviews far more than "we'll add it later." [Go-IoT](https://dingo-iot.io/iot-bacnet/)[GitHub](https://github.com/entraeiendom/_archieved_bacnet-azure-iot)

**5. Add Infrastructure as Code from day one**

The scaffold includes a `main.bicep` placeholder but marks it as optional. It isn't optional. Every time you tear down and recreate your Azure environment (and you will, to keep costs low between work sessions), provisioning six services manually from the CLI takes 20–30 minutes and introduces configuration drift. A Bicep file that stands up the entire stack in one command is also an excellent portfolio artifact — it shows you understand infrastructure as code, which directly maps to the DevOps practices Schneider values. Make it required in Milestone 1.

**6. Add a local development mode that eliminates Azure costs entirely**

Right now the scaffold assumes you're running against live Azure services even during development. That's $15–25/month minimum even with careful tier selection. A better approach: add a `--local` flag to the C++ agent that writes JSON to a local file instead of IoT Hub, and have the C# API optionally read from that file via a `FileSystemListenerService` that mirrors `IoTHubListenerService`. This costs nothing, removes network latency from your dev loop, and is a common production pattern (local vs. cloud environments) that's worth demonstrating anyway.

**7. The go-to-market timeline is too optimistic**

The previous analysis suggested getting a paying customer in months 3–6. That's possible but compressed. HVAC contractors are relationship-driven businesses — they don't respond to cold emails, they respond to people they know or referrals from people they trust. A more realistic path: months 3–6 should be finding 2–3 contractors willing to let you install a free pilot at one of their client buildings. Months 6–9 is operating those pilots, fixing the inevitable issues, and collecting testimonials. First paying customer in months 9–12 is more honest.

---

### Risk Register

**R1 — IoT Hub quota exhaustion (High likelihood, Medium impact)**  
As described above, the F1 tier's 8,000 daily messages at 0.5KB chunk size will be exhausted in hours at any realistic sensor frequency. Mitigation: batch multiple sensor readings per MQTT payload, move to S1 from day one, implement quota exception handling in the C++ agent with local message queuing as fallback.

**R2 — BACnet integration complexity (High likelihood, High impact)**  
BACnet/SC (Secure Connect) is becoming the default for new installations, especially in finance, healthcare, and government, meaning legacy BACnet/IP and MS/TP aren't going away but the protocol landscape is fragmenting. Real HVAC controllers use proprietary object models, inconsistent implementations, and firmware-level quirks that no open-source library fully handles. Mitigation: keep the edge agent abstracted behind an `ISensorReader` interface so you can swap protocol implementations without rewriting the cloud pipeline. Plan for 4–8 weeks of BACnet integration work when you reach a real building, not 4–8 hours. [Microsoft Azure Marketplace](https://azuremarketplace.microsoft.com/en/marketplace/apps/iotium.azureiot-bacnet-inode?tab=overview)

**R3 — Azure cost creep (High likelihood, Medium impact)**  
Running all six Azure services simultaneously, even at development scale, costs $30–60/month once you're off free tiers. IoT Hub S1 + Container App with 1 warm replica + PostgreSQL Flexible Server Burstable + SignalR Standard is the main cost stack. Mitigation: implement the local development mode above, and use Azure Cost Alerts to notify you at $20/month. For each paying customer, the per-building Azure cost should be modeled explicitly — the scaffold should include a cost-per-building section.

**R4 — Solo developer bandwidth (Medium likelihood, High impact)**  
This is the most likely thing to quietly kill the project. Working a full-time job, building EdgeMonitor evenings and weekends, and pursuing job interviews simultaneously is three things at once. The milestones are well-sequenced but 6 weeks per group of milestones assumes consistent 10–15 hours per week of focused work. Real life rarely cooperates. Mitigation: treat Milestone 1 as the only milestone that matters until it ships. Don't start Milestone 2 until Milestone 1 is done and committed. Scope creep within milestones is what actually kills side projects.

**R5 — C++ skills gap (Medium likelihood, Medium impact)**  
The scaffold assumes C++ is already comfortable enough to write a production-quality IoT agent using the Azure IoT C SDK. If your C++ is primarily academic (the hex grid game) and your daily work is C#, the Azure IoT C SDK's memory management patterns, CMake build configuration, and cross-compilation for ARM targets will take real time to learn. Mitigation: spend 1–2 weeks before starting Milestone 1 working through a simpler Azure IoT C SDK sample (just publishing a hardcoded JSON string to IoT Hub). The learning is front-loaded and the actual agent code is manageable once you understand the SDK patterns.

**R6 — SignalR free tier connection cap (High likelihood, Low impact)**  
20 concurrent connections sounds like enough for a portfolio project, but demos have a way of hitting unexpected limits. If you're doing a live demo to a Schneider interviewer and someone else opens the dashboard on their phone to follow along, you may hit the cap. This is low impact because it's cheap to fix ($40/month to Standard tier) and easy to anticipate. Mitigation: document it in your scaffold, upgrade to Standard before any live demo, and treat the cost as part of your "demo budget."

**R7 — Direct-to-SMB sales difficulty (Medium likelihood, High impact)**  
This was flagged in the previous analysis but deserves sharper framing: small business owners don't buy software categories they don't understand. "Building management system" means nothing to a restaurant owner. "Get a text if your walk-in cooler breaks overnight" means everything to them. The product is fine — the messaging and sales motion is the risk. Mitigation: reframe the go-to-market around HVAC contractor channel sales from day one, not direct SMB sales. The product language in your dashboard should also be written for building owners, not engineers. "Zone 2 is running 12% hotter than last week" not "temperature delta threshold exceeded."

**R8 — Large competitor entry (Low likelihood, High impact)**  
Schneider, Honeywell, and Johnson Controls all have SMB-adjacent offerings and could decide to productize downmarket at any time. The top three players in building automation hold roughly 21% of total market share — meaning the market is fragmented enough that they've historically ignored the SMB segment. That's your window. Mitigation: move fast, establish customer relationships early (relationships are a moat even when technology isn't), and build on open protocols (BACnet, MQTT) so you're not competing on proprietary lock-in. [Renub](https://www.renub.com/building-management-system-market-p.php)

**R9 — On-site hardware variability (Low likelihood in dev, High impact in production)**  
Bivocom's BACnet IoT gateway research confirms that real buildings have BACnet devices from dozens of manufacturers with inconsistent behavior, network topology surprises (MS/TP on RS-485 versus BACnet/IP), and firmware versions that haven't been updated in a decade. A clean simulation runs perfectly; a real building does not. This doesn't affect portfolio work at all, but it's the largest risk to commercial viability. Mitigation: when you reach a real building, bring a BACnet sniffer (Wireshark with BACnet plugin works) and budget double your estimated integration time. Your first paid customer will be the most expensive customer you ever have. [Microsoft Azure Marketplace](https://azuremarketplace.microsoft.com/en/marketplace/apps/iotium.azureiot-bacnet-inode?tab=overview)

---

The most important single improvement is the local development mode — it removes the financial friction that causes side projects to stall, and lets you build at full speed without watching an Azure bill accumulate during a period when the project is generating zero revenue. Get that in before you write a line of sensor simulation code.
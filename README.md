What is this project
====================

Watch the YouTube videos to learn more about the project: 
* 2026-01-23 - Explanation of the Asynchroneous Message Bus architecture, Function Calling JSON deep dive, demo running with a local free Ollama LLM - https://youtu.be/hHEtT9VM4Os
* 2026-01-20 - Demo of GAIA running on iOS using GroqCloud LLMs - https://youtu.be/a0ZdeBmG070
* 2025-12-27 - Why I abandoned Gemini API - https://youtu.be/_kgB33y2OTM

This is a fully featured, free*, functional UNITY 6 AI project. Features:
* NPC with speech interface: using STT+LLM+TTS cloud services
* NPC expressions/emotions using blendshapes
* Speech to Text providers supported: GroqCloud(OpenAI Whisper)
* LLM with vision and function calling supported: GroqCloud, Ollama
* Text to Speech supported: Speechify
* Various function calling tools: Google Websearch, Weather service, Maps, GPS, open URL
* Communication to remote services using REST API, internal component communication using an async Message Bus

(* => excluding paid cloud services for the NPC and assumes you don't exceed the Vivox Voice services complementary service tresholds)

What is new in this Branch
==========================
* Supports FUNCTION CALLING capabilities of LLM services, allows for more interactive conversations
* Completely rearchitected backend using an asynchroneous MessageBus to support function calling and direct AI component communication
* Both Ollama and GroqCloud components support Vision and Function Calling
* Tools: gps, web search, web crawl, open webpage, weather, map 


Steps to get started
====================
1. Pull the branch
2. Open Unity Editor, when prompted about Errors, select Ignore and do not start in safe mode
3. Go to File -> Open Scene -> BaMMain 
4. Import the uLipSync package: Top Menu -> Assets -> Import Package -> Custom Package -> Navigate to the uLipSync package in this repository and import it
5. If you want the InGame Debug Console then go to the Unity Store and purchase it (free)
6. Be sure to get API keys from the following cloud providers: HuggingFace (free), GroqCloud (free), Speechify (paid), RapidAPI (partially free) and Sloyd.ai (partially free).
7. Add the API Keys in /Assets/Resources/Secure/APIKeys.txt with the following lines (no bullets)
* Google_API_Key:YourKeyHere
* Google_SearchID:YourKeyHere
* Groq_API_Key:YourKeyHere
* Speechify_API_Key:YourKeyHere
* HF_API_Key:YourKeyHere
* ElevenLabs_API_Key:YourKeyHere
* Rapid_API_Key:YourKeyHere


Compatibility
=============
* Currently this code is tested for iOS, Android and MacOS. Windows should work fine but must add Windows Build Profile
* It should run perfectly on a Mac, Android and iOS (to be tested)

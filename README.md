What is this project
====================
This is a fully featured, free*, functional UNITY 6 AI project. Features:
* NPC with speech interface: using STT+LLM+TTS cloud services
* Speech to Text providers supported: GroqCloud(OpenAI Whisper), HuggingFace (ElevenLabs to be added soon)
* LLM supported: GroqCloud, Google Gemini, Ollama
* Text to Speech supported: Speechify, ElevenLabs
* RAG supported: Google Web Search

(* => excluding paid cloud services for the NPC and assumes you don't exceed the Vivox Voice services complementary service tresholds)

What is new in this Branch
==========================
* This branch is running on UNITY 6000.1.12f1 (July 2025)
* AI API keys are stored in a file in Assets/Resources/Secure (<b>not</b> included)
* Google LLM has a version with <b>vision</b> which is able to interpret webcam/camera images


Steps to get started
====================
1. Pull the branch
2. Open Unity Editor, when prompted about Errors, select Ignore and do not start in safe mode
3. Go to File -> Open Scene -> BaMMain 
4. Import the uLipSync package: Top Menu -> Assets -> Import Package -> Custom Package -> Navigate to the uLipSync package in this repository and import it
5. If you want the InGame Debug Console then go to the Unity Store and purchase it (free)
6. Be sure to get API keys from the following cloud providers: HuggingFace (partially free), GroqCloud (partially free), Speechify (partially free).
7. Add the API Keys in /Assets/Resources/Secure/APIKeys.txt with the following lines (no bullets)
* Google_API_Key:YourKeyHere
* Google_SearchID:YourKeyHere
* Groq_API_Key:YourKeyHere
* Speechify_API_Key:YourKeyHere
* ElevenLabs_API_Key:YourKeyHere


Compatibility
=============
* Currently this code is tested for iOS, Android, Windows and WebGL

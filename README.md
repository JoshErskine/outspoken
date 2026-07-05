# Outspoken

**Dictation without limits.** A Windows push-to-talk dictation tool: hold a hotkey anywhere, speak, release — your words land at the cursor, cleaned up. Speech-to-text runs **locally** ([Whisper](https://github.com/openai/whisper) via whisper.cpp), so your voice never leaves your machine and there are no word caps. A fast LLM pass tidies the transcript (fillers, false starts, punctuation) without changing what you said.

Built as a daily tool first — replacing a subscription dictation app — and as a portfolio piece: .NET on Windows-on-ARM64 (Snapdragon X Elite), local AI inference, and an AI-assisted build process where every commit is human-authored and agent-co-authored.

## Status
🚧 Early build — following a staged pipeline (spec → ADRs → plan → build). The walking skeleton (raw dictation end-to-end) is the first milestone.

## Principles
- **Private by architecture:** audio is memory-only; the only network call is the text-cleanup request on my own API key. No telemetry, no stored dictations.
- **Never lose words:** if injection into the focused app fails, the text stays on the clipboard.
- **Feels instant:** ~2s ceiling from key-release to text-at-cursor.

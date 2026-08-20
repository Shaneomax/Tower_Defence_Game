# LocalNPC — Developer Build Guide

**For:** Anik — lead implementer
**Reviewer:** Fahim — architecture, API design, code review
**Assets:** Rakib — demo character, visemes, demo scene
**Version 4.0 · 2026-08-19 · This document replaces all previous engineering docs.**

Build an on-device conversational NPC package for Unity. Player speaks → character replies out loud with lip sync. All inference local. **Windows x64 first. VR is Phase 2. Multi-language architecture from Step 1, English the only shipping language in v1.**

---

# PART 0 — How to use this document

## 0.1 Your job

You own roughly 60% of the codebase. Fahim owns architecture, review, and the subtle-logic files. Every file in §6 has an owner column — build the ones marked **A**.

## 0.2 How to work through it

1. Read **Part 1–4** fully before writing any code. Once.
2. Complete **Part 2** (environment) until `Environment Check` is green.
3. Work **Part 9** in order. It is numbered Step 1 → Step 47. Do not skip ahead; later steps assume earlier ones exist.
4. Each step has **Goal / Files / How / Acceptance / Common mistakes.** A step is done when Acceptance passes — not when the code compiles.
5. Open a PR per step (or per small group). Fahim reviews within 48 hours. If a review is stalling you past 48 hours, say so in the daily channel — do not sit blocked and do not merge unreviewed.
6. When something breaks, check **Part 12** (debugging playbook) before asking. Most failures in this architecture are already listed there.

## 0.3 Rules that override everything else

| # | Rule |
|---|---|
| 1 | If a Phase-0 spike contradicts a decision here, **the spike wins** — tell Fahim, update the doc the same day. |
| 2 | Do not create a script that isn't in §6 without adding it to §6 first. |
| 3 | **No Android/VR code before v1.0 ships.** Not even stubs, outside `Runtime/Platform/`. |
| 4 | **No language-specific logic outside a `LanguageProfile`-driven implementation class.** See Part 4. |
| 5 | "I don't know" is a valid answer. It becomes a timeboxed spike, not a guess. |

## 0.4 Status tags
`TODO` `WIP` `BLOCKED` `REVIEW` `DONE` `CUT`

---

# PART 1 — What we are building

## 1.1 The product in one paragraph

A Unity package. A developer drops a `NpcAgent` component onto a character, assigns a `NpcPersonality` asset containing a system prompt, presses Play, and speaks into a microphone. The character replies with synthesized speech and lip-synced mouth movement. Everything runs on the player's machine. The only network call in the entire product is the one-time model download.

## 1.2 Pipeline in one line

```
Mic → VAD → Speech-to-Text → LLM → sentence splitter → Text-to-Speech → audio + lip sync
```

The trick that makes it feel fast: **each finished sentence goes to TTS while the LLM is still generating the next one.** Waiting for the full reply costs 2–4 seconds. Streaming per sentence gets us under 1 second.

## 1.3 Platform targets

| Target | Phase | Notes |
|---|---|---|
| Windows x64 — Editor, Mono, IL2CPP | **1** | Only supported target for v1.0 |
| Quest 2 / 3 / 3S (Android arm64) | 2 | Part 15 |
| Pico 4, Vive XR Elite | 2 | Same arm64 binary, untested |
| Android tablets (6 GB+) | 2 | Same arm64 binary |
| macOS / Linux | Later | Native lib for those targets only |
| Android phones | Never | 4 GB devices OOM; thermal throttling |
| iOS | Later | arm64 static lib; jetsam pressure at ~600 MB resident |
| WebGL | Never (this architecture) | No native plugins, ~2 GB heap cap, threads need COOP/COEP |
| Consoles | Never | No native plugin freedom |

## 1.4 Model capability ceiling — design around it

A 0.5B model produces believable short-turn characters: shopkeeper, guard, guide, receptionist. It will contradict itself over long conversations and has unreliable world knowledge.

Engineering consequences you must build in:
- Default cap of 2–3 sentences per reply (`NpcPersonality.maxReplySentences`)
- System prompts under ~200 tokens; inspector shows a live count and warns above that
- Aggressive history trimming (Step 18)
- Three model presets so quality is a user-tunable axis (§1.6)

## 1.5 Out of scope for v1.0

Function calling · long-term memory beyond a rolling summary · voice cloning · emotion/prosody control · dialogue-tree authoring UI · networked conversation sync · any Android or XR code · **any language other than English shipping as working.**

## 1.6 Model presets

| Preset | Model | Download | Min RAM |
|---|---|---|---|
| `Fast` (default) | Qwen2.5-0.5B-Instruct Q4_K_M | ~400 MB | 8 GB |
| `Balanced` | Qwen2.5-1.5B-Instruct Q4_K_M | ~1.0 GB | 16 GB |
| `Quality` | Qwen2.5-3B-Instruct Q4_K_M | ~2.0 GB | 16 GB |

0.5B and 1.5B are Apache-2.0. **The 3B licence differs — Fahim verifies in Step 3 before we ship it.**

---

# PART 2 — Language policy — read this before Part 4

## 2.1 The honest position

**The architecture is multilingual from Step 1. The product ships English only in v1.0.**

These are not in conflict. Language-specific behaviour is routed through a `LanguageProfile` asset from the very first file you write, so adding a language later is *config plus data plus one implementation class* — never a refactor. But no second language ships in v1, and Bangla specifically has two blockers that are research questions, not tasks.

## 2.2 Difficulty by stage — this is why

| Stage | Language dependence | Bangla specifically |
|---|---|---|
| **VAD** (Silero) | **None.** Acoustic detection is language-agnostic | Works today, zero changes |
| **STT** (Whisper) | Model choice only | **Medium.** `tiny.en` is English-only; multilingual Whisper covers Bengali but it's low-resource in training — `tiny` multilingual on Bengali is poor. Needs `small` (~250 MB int8) or a Bengali fine-tune. Cost is size and latency, not architecture |
| **LLM** (Qwen2.5) | Model choice + chat template | **Hard at 0.5B.** Multilingual ability collapses at small parameter counts — broken grammar, case errors. Realistically needs 3B+ or a Bangla fine-tune (TituLLM / Bangla-LLaMA family). Desktop RAM makes this solvable |
| **TTS** (Piper) | **Voice model + G2P + text normalisation, all per-language** | **The blocker.** §2.3 |
| **UI / subtitles** | Font and text shaping | Bangla is a complex script — conjuncts, reordering vowels. TextMeshPro Indic shaping needs its own verification |

## 2.3 Why TTS is the blocker — the part that can't be scheduled

**Problem 1 — voice availability.** A permissively-licensed Piper voice must exist for the language. I cannot confirm a Bengali Piper voice exists. **Someone must check the Piper voice list directly.** If none exists, training one is a separate project with its own data requirements.

**Problem 2 — G2P does not generalise, and our GPL escape route is English-shaped.**

Our TTS phonemization avoids espeak-ng because espeak-ng is **GPL-3.0** and shipping it inside a paid closed-source Asset Store package is a licence violation. Our replacement is CMUdict plus English letter-to-sound rules. CMUdict is *an English pronunciation dictionary.*

Bangla is an abugida — inherent vowels, schwa deletion rules, conjunct clusters. Non-trivial G2P. **And the standard open solution for non-English G2P is espeak-ng — the exact library we are avoiding.**

So the licensing workaround does not generalise. Every new language re-opens the phonemization problem from scratch.

## 2.3b Language difficulty is tiered — "Bangla" and "other languages" are different questions

| Language group | Difficulty | Why |
|---|---|---|
| Spanish, French, German, Italian, Portuguese | **Easy-ish** | Good Piper voices; large open pronunciation lexicons; Whisper strong; Qwen2.5 handles them at 1.5B |
| Hindi, Arabic, Thai, Vietnamese | **Medium-hard** | Voices vary; harder G2P; weaker Whisper; script shaping work |
| **Bangla** | **Hardest of the ones we'd want** | Low-resource in Whisper, collapses at 0.5B, complex script, and the G2P/licence problem |

So "can it do other languages" is a stronger yes than "can it do Bangla." A European language is likely a few days of work once the seams exist.

## 2.3c Two routes to non-English phonemization

**Route 1 — permissive, per language.** Write a dictionary + rules phonemizer for each language, as we do for English. Correct, safe, and expensive: 60–100 h for Bangla alone.

**Route 2 — public interface + externally distributed GPL adapter.** Ship `IPhonemizer` as **public API** (§7.4). A separately distributed GPL-licensed adapter implements it over espeak-ng. We never ship GPL code inside the paid package; the buyer installs the add-on. **If this holds legally, it unlocks 100+ languages including Bangla, without us writing any G2P.**

Route 2 is far cheaper and needs a qualified legal read before we rely on it. **Spike E (Step 3B) gathers the facts; Fahim gets the legal answer.** Build the public interface regardless — it is good design on its own merits.

## 2.4 What this means practically

- **Build:** language-neutral architecture, one `LanguageProfile` asset (`en-US`), **public `IPhonemizer` / `ITextNormalizer`**, documented procedure for adding more. Part 4, §7.4, Part 14.
- **Run:** Spike E (Step 3B) in Stage A — 8 hours that replace four open questions with four measured facts.
- **Do not build:** a second `LanguageProfile`, any non-English model, any Bangla logic, Indic font work — until Spike E and the legal read come back.
- **Before any language is promised publicly:** the gate in Part 14 must be answered.

**If a PR contains `bn-BD` outside a test fixture or a comment, it is out of scope and gets rejected.** The failure mode here is that "make it ready" quietly becomes "start making it work," and six hours turns into sixty.

## 2.5 Bangla — indicative scope if ever pursued

Planning only. Do not schedule until Part 14 questions 1 and 2 are answered.

| Work | Est |
|---|---|
| Verify or source a Bengali Piper voice (or train one) | 20 h — or a separate project |
| Bangla G2P without GPL: schwa deletion, conjuncts, inherent vowels | 60–100 h |
| Bangla text normalisation (numbers, dates, currency) | 20 h |
| Whisper Bengali model selection + accuracy evaluation on real audio | 20 h |
| LLM selection for Bangla output quality at a usable size | 25 h |
| TextMeshPro Bangla shaping verification and font work | 15 h |
| Integration, profile, testing | 20 h |
| **Total** | **180–220 h, with two unresolved research risks at the front** |

That is comparable to the entire English build through Step 30. Treat it as a distinct project, ideally triggered by a real client requirement rather than built speculatively.

---

# PART 3 — Environment setup

Finish this before Step 1. Everyone runs identical versions.

## 3.1 Install

| Tool | Version | Notes |
|---|---|---|
| Unity | **6000.0.x LTS** | Exact patch pinned in `ProjectSettings/ProjectVersion.txt` |
| Unity module | Windows Build Support (**IL2CPP**) | We test IL2CPP every phase, not at the end |
| Visual Studio 2022 | 17.8+ | Workloads: .NET desktop **and** Desktop development with C++ (needed for debugging native crashes even if we use prebuilt binaries) |
| Git + Git LFS | latest | Run `git lfs install` before first clone |
| .NET SDK | 8.0 | Tooling scripts |
| Python | 3.11 | Model conversion scripts in `models/` |

## 3.2 Unity packages — pin these

```
com.unity.ai.inference          ← Unity Inference Engine (formerly Sentis). PIN THE EXACT VERSION.
com.unity.test-framework        1.4.x
com.unity.ide.visualstudio
```

**Never float the Inference Engine version.** Sentis was renamed to Inference Engine at 2.2 / Unity 6.2, and API drift between minor versions has broken tensor allocation and disposal patterns. A version bump is its own PR with a full test run.

## 3.3 Clone and open

```bash
git clone <repo> localnpc
cd localnpc
git lfs install
git lfs pull            # must materialise real files, not pointer stubs
```

Open `localnpc/unity/` in Unity Hub with the exact pinned editor version.

## 3.4 Editor settings that must match exactly

| Setting | Value | Why |
|---|---|---|
| Api Compatibility Level | .NET Standard 2.1 | Asset Store compatibility |
| **Allow 'unsafe' Code** | **On** | Required for zero-alloc audio buffers |
| Scripting Backend (test builds) | Mono **and** IL2CPP | IL2CPP strips differently |
| Managed Stripping Level | Low | Higher levels strip reflection used by ScriptableObject serialization |
| Audio → DSP Buffer Size | Best Latency | Mic responsiveness |
| Active Input Handling | Both | Samples must work with either input system |

## 3.5 Verify

Run `LocalNPC → Developer → Environment Check` (you build this in Step 4). It asserts: Unity version, Inference Engine version, unsafe code on, LFS files real, native DLL present and loadable, dev models cached.

**Green on this check is the definition of "ready to work."** If it's red, fix it before writing code — a mismatched environment produces bugs that look like code bugs and waste days.

---

# PART 4 — Architecture you must understand before coding

## 4.1 Layers — dependencies point down only

```
┌───────────────────────────────────────────────────────┐
│ Presentation   NpcAgent · inspectors · samples        │
├───────────────────────────────────────────────────────┤
│ Orchestration  NpcPipeline · state machine ·          │
│                ConversationSession · interrupt        │
├───────────────────────────────────────────────────────┤
│ Contracts      IVoiceActivityDetector · ISpeechRecognizer │
│                ILanguageModel · ISpeechSynthesizer ·  │
│                ITextNormalizer · IPhonemizer          │
├───────────────────────────────────────────────────────┤
│ Implementations  Silero · Whisper · llama · Piper ·   │
│                  EnglishTextNormalizer · Cmudict      │
├───────────────────────────────────────────────────────┤
│ Platform       NativeLibraryLoader ·                  │
│                PlatformCapabilities · ThreadDispatcher│
└───────────────────────────────────────────────────────┘
```

**An implementation never references `NpcPipeline`. `NpcPipeline` never references `WhisperRecognizer`.** Violations get rejected in review. This rule is the entire difference between Phase 2 taking 6 weeks and taking 6 months — and between adding a language taking 2 days and 2 months.

## 4.2 Full runtime pipeline

```
Microphone
   │ main thread, Update()
   ▼
MicrophoneCapture ──► AudioRingBuffer   (lock-free SPSC, float32)
                            │
        ════════ thread: LocalNPC-Audio ════════
                            ▼
                     AudioResampler        48k/44.1k → 16k mono
                            ▼
                     AudioPreprocessor     DC removal, RMS normalise
                            ▼
                     SileroVadModel        512-sample frames → p(speech)
                            ▼
                     VoiceActivityGate     hysteresis → utterance float[]
                            │
        ════════ thread: LocalNPC-Inference ════════
                            ▼
                     LogMelSpectrogram → WhisperRecognizer → SttPostProcessor
                            ▼ transcript
                     ConversationSession.AddUserTurn()
                            ▼
                     PromptBuilder → token[]
                            ▼
                     LlamaBackend.GenerateAsync
                            │ token deltas
                            ├─► StopSequenceDetector
                            └─► SentenceSplitter ────┐
                                                     ▼ one sentence
                                        ITextNormalizer  (LanguageProfile)
                                                     ▼
                                        IPhonemizer      (LanguageProfile)
                                                     ▼
                                        PiperSynthesizer → float[] PCM
                                                     │
        ════════ back to main via ThreadDispatcher ════════
                                                     ▼
                                        AudioClipBuilder (pooled)
                                                     ▼
                                        SpeechAudioQueue → AudioSource
                                                     ▼
                                        IVisemeDriver → blendshapes
```

## 4.3 Threading — three rules you must never break

| Thread | Owns | Never does |
|---|---|---|
| Unity main | Mic read, `AudioClip` creation, `AudioSource`, events, blendshapes | Any inference, file I/O, or blocking lock |
| `LocalNPC-Audio` | Resample, preprocess, VAD | Touch any `UnityEngine.Object` |
| `LocalNPC-Inference` | STT, LLM, TTS — serialised work queue | Touch any `UnityEngine.Object` |

**Two worker threads, not four.** A separate TTS thread saves ~40 ms and buys a race condition. The Phase 1 answer is no.

**Unity API from a worker thread is forbidden.** `Debug.Log`, `Time.time`, `Application.persistentDataPath`, `UnityEngine.Random` — all main-thread-only or unsafe. Cache what you need at startup. `LocalNpcLog` buffers worker messages and flushes on main.

**All worker→main handoff goes through `ThreadDispatcher.Enqueue(Action)`,** drained in `Update()`. Nothing else crosses.

## 4.4 State machine

```
Unloaded ─► Loading ─► Idle ─► Listening ─► Transcribing ─► Thinking ─► Speaking ─┐
                        ▲                                                          │
                        └──────────────────────────────────────────────────────────┘
                                     (+ Error from any state)
```

| From | To | Trigger |
|---|---|---|
| `Unloaded` | `Loading` | `PrewarmAsync()` or first use |
| `Loading` | `Idle` / `Error` | load complete / failure |
| `Idle` | `Listening` | `StartListening()` |
| `Idle` | `Thinking` | `SayAsync(text)` — text path skips STT |
| `Listening` | `Transcribing` | gate emits an utterance |
| `Listening` | `Idle` | `StopListening()` |
| `Transcribing` | `Thinking` | non-empty transcript |
| `Transcribing` | `Idle` | empty transcript after post-processing |
| `Thinking` | `Speaking` | first synthesized clip queued |
| `Speaking` | `Idle` | queue drained + generation complete |
| `Speaking` | `Listening` | barge-in, mode `StopAndListen` |
| any | `Idle` | `Interrupt()` |
| any | `Error` | unrecoverable stage failure |

Illegal transition: **throw** under `UNITY_EDITOR`, **log error + force `Idle`** in builds. Never silently continue — that produces the "NPC is stuck and nobody knows why" bug class.

## 4.5 Data contracts between stages

| Boundary | Type | Rules |
|---|---|---|
| Mic → ring buffer | `float[]` interleaved, device rate | Producer never blocks; drops oldest on overflow, increments a counter |
| Ring → resampler | `Span<float>` view | No copy |
| Resampler → VAD | `float[512]` @16 k mono | Exactly 512; VAD is stateful across frames |
| Gate → STT | `float[]` @16 k, 0.3–30 s | Includes pre-roll padding |
| STT → pipeline | `string` | Already post-processed; may be empty → back to `Idle` |
| LLM → splitter | `string` deltas | **Guaranteed valid UTF-8** by `LlamaTokenStream` |
| Splitter → TTS | `string` sentence | Normalisation happens downstream, not here |
| TTS → audio | `float[]` @22050 mono | **Never `AudioClip`** — workers can't create Unity objects |
| Audio → visemes | `VisemeFrame[]` or amplitude | Main thread only |

## 4.6 Cancellation

One `CancellationTokenSource` per turn, owned by `NpcPipeline`. Every stage takes the token.

- LLM checks **between token steps**, calls `lnpc_request_cancel` natively
- Whisper checks between decoder steps
- Piper checks before each sentence (mid-synthesis cancellation isn't worth the complexity — a sentence is ~100 ms)
- `SpeechAudioQueue.Flush()` stops the `AudioSource` within one frame
- **Total observable stop time: < 100 ms**

`Interrupt()` must be safe from any state and any thread.

## 4.7 Language neutrality — the design rule

**Every language-specific behaviour is reached through a `LanguageProfile`.** There are exactly five places language matters, and each one is an interface or a data field:

| Language-specific thing | Where it lives | Never |
|---|---|---|
| Sentence terminators, abbreviations | `LanguageProfile` data | Hardcoded `.!?` in `SentenceSplitter` |
| Number/currency/ordinal expansion | `ITextNormalizer` implementation | A static `TextNormalizer` class |
| Grapheme→phoneme | `IPhonemizer` implementation | Calling CMUdict directly from the synthesizer |
| Whisper language token | `LanguageProfile.whisperLanguageCode` | Hardcoded `<\|en\|>` |
| Voice model | `LanguageProfile.defaultVoice` | Hardcoded voice asset reference |

**Universal, not language-specific:** markup stripping (`**bold**`, emoji, list markers). The LLM emits markdown in every language. That lives in a shared `MarkupStripper` called by every normalizer — do not duplicate it per language.

You will build the `en-US` profile and the English implementations. That's it. But you build them *behind these seams from Step 1*, not bolted on later.
---

# PART 5 — Repository and package structure

## 5.1 Repo

```
localnpc/
├── unity/                       # Unity 6 LTS dev project
│   ├── Assets/
│   │   ├── LocalNPC/            # the package, developed in place
│   │   └── _Dev/                # dev scenes, scratch — never shipped
│   ├── Packages/manifest.json   # Inference Engine version PINNED here
│   └── ProjectSettings/
├── package/                     # UPM staging, generated by PackageExporter
├── native/                      # C++ sources — only if the spike in Step 2 fails
├── models/                      # conversion + quantisation scripts (Python). NOT weights.
├── tools/                       # build_windows.ps1, export_package.ps1, run_tests.ps1
├── docs/                        # developer manual source
└── BUILD-GUIDE.md               # this document
```

## 5.2 Git

**Branches:** `main` (always releasable) · `dev` (integration) · `feat/anik-<topic>` · `fix/anik-<issue>`

**Merge rules:** `feat/*` → `dev` needs EditMode tests green + Fahim's review. `dev` → `main` needs full suite green on Mono **and** IL2CPP.

**Commits:** `<area>: <what changed>` — e.g. `audio: fix ring buffer wraparound on partial read`.
Areas: `core audio vad stt llm tts lang lipsync models platform editor tests ci docs`

**`.gitattributes`:**
```
*.onnx  filter=lfs diff=lfs merge=lfs -text
*.gguf  filter=lfs diff=lfs merge=lfs -text
*.fbx   filter=lfs diff=lfs merge=lfs -text
*.wav   filter=lfs diff=lfs merge=lfs -text
*.psd   filter=lfs diff=lfs merge=lfs -text
*.mp4   filter=lfs diff=lfs merge=lfs -text
*.unity text eol=lf
*.asset text eol=lf
```

**Never commit production model weights.** Test fixtures only, under 10 MB each.

## 5.3 Package layout

```
Assets/LocalNPC/
├── package.json  README.md  CHANGELOG.md  LICENSE.md  Third-Party Notices.md
├── Runtime/
│   ├── LocalNPC.Runtime.asmdef
│   ├── Core/                (17)
│   ├── Audio/               (9)
│   ├── Inference/
│   │   ├── Vad/             (3)
│   │   ├── Stt/             (7)
│   │   ├── Llm/             (8)
│   │   └── Tts/             (8)
│   ├── LipSync/             (5)
│   ├── Models/              (6)
│   └── Platform/            (3)
├── Editor/                  (10)
├── Tests/
│   ├── EditMode/            (8)
│   ├── PlayMode/            (5)
│   └── Fixtures/            (LFS)
├── Plugins/x86_64/          # .dll — Phase 1 only
├── Resources/               # LanguageProfile_en-US.asset
├── Prefabs/  Materials/
└── Samples~/
    ├── 01_QuickStart/
    ├── 02_Shopkeeper/
    └── 03_Kiosk/
```

## 5.4 Assembly definitions — 5

| asmdef | References | Platforms |
|---|---|---|
| `LocalNPC.Runtime` | `Unity.InferenceEngine` | Any |
| `LocalNPC.Editor` | `LocalNPC.Runtime` | Editor only |
| `LocalNPC.Samples` | `LocalNPC.Runtime` | Any (in `Samples~`) |
| `LocalNPC.Tests.EditMode` | Runtime, `nunit.framework` | Editor |
| `LocalNPC.Tests.PlayMode` | Runtime, `nunit.framework` | Editor + Standalone |

uLipSync is **optional and never bundled**. In `LocalNPC.Runtime.asmdef`:
```json
"versionDefines": [
  { "name": "jp.ne.hibara.ulipsync", "expression": "", "define": "LOCALNPC_ULIPSYNC" }
]
```
`ULipSyncBridge.cs` is entirely inside `#if LOCALNPC_ULIPSYNC`.

## 5.5 Namespaces

```
LocalNPC                  → NpcAgent, NpcPersonality, NpcState, LanguageProfile, LocalNpc
LocalNPC.Audio            → capture, resample, VAD gate, playback
LocalNPC.Inference        → the stage interfaces
LocalNPC.Inference.Vad    → Silero, energy fallback
LocalNPC.Inference.Stt    → Whisper
LocalNPC.Inference.Llm    → llama backend
LocalNPC.Inference.Tts    → Piper, normalizers, phonemizers
LocalNPC.LipSync          → viseme drivers
LocalNPC.Models           → catalog, downloader, provisioner
LocalNPC.Platform         → native loader, capabilities, dispatcher
LocalNPC.Editor           → all editor code
```

**No vendor name in the public `LocalNPC` namespace.** `NpcAgent` must never expose `LlamaSession`, `Tensor`, or a Piper type. This is what lets backends change without breaking buyers.

---

# PART 6 — Script inventory — 89 files

**Owner:** A = you · F = Fahim · F/A = Fahim designs API, you implement · R = Rakib
**Step** = which step in Part 9 builds it.

Do not create a file not listed here without adding it here first.

## 6.1 Runtime/Core — 17

| # | File | Own | Step | Purpose & notes |
|---|---|---|---|---|
| 1 | `INpcModules.cs` | F | 5 | All stage contracts. Written before any implementation — see §7.2 |
| 2 | `LocalNpcLog.cs` | A | 6 | Levelled logging behind `LOCALNPC_VERBOSE`. Thread-safe: worker messages queue, flush on main. Prefix `[LocalNPC]`. **No other file may call `Debug.Log`** |
| 3 | `NpcState.cs` | F | 5 | Enum + `NpcStateMachine` with a static legal-transition table. Throws in Editor, logs+forces `Idle` in builds |
| 4 | `NpcEvents.cs` | F | 5 | C# events + `UnityEvent` mirrors. All dispatch on main thread. **Wrap every subscriber call in try/catch** — a buyer's exception must not break the pipeline |
| 5 | `LanguageProfile.cs` | A | 7 | **The multilingual seam.** ScriptableObject: BCP-47 tag, terminators, closers, abbreviations, normalizer kind, phonemizer kind, Whisper code, default voice. Factory methods. §7.3 |
| 6 | `NpcPersonality.cs` | F | 18 | ScriptableObject: displayName, systemPrompt, greeting, few-shot pairs, voice, sampling overrides, stopStrings, maxReplySentences, **`language` (BCP-47)**. Cache token count in a `[NonSerialized]` field invalidated on `OnValidate` |
| 7 | `NpcRuntimeConfig.cs` | F | 18 | ScriptableObject, one per project: preset, contextLength, threadCount (0 = auto), storage mode, diagnostics, default interrupt mode, **default `LanguageProfile`** |
| 8 | `ConversationTurn.cs` | A | 18 | `readonly struct`: role, text, tokenCount, timestamp |
| 9 | `ConversationSession.cs` | A | 18 | History + trimming. **Always keep system prompt + few-shot; drop oldest user/assistant *pairs*, never a lone turn** (a lone turn corrupts the chat template). Reserve 256 tokens for the reply |
| 10 | `PromptBuilder.cs` | F | 19 | Renders persona + few-shot + history into the chat template. Template is data-driven. **Must emit a byte-stable prefix across turns** or prefix caching silently dies and latency doubles |
| 11 | `SentenceSplitter.cs` | F | 20 | Streaming chunker. **Reads terminators/closers/abbreviations from `LanguageProfile` — no hardcoded `.!?`.** Handles decimals, ellipsis, closers, max-length flush. Test-first |
| 12 | `StopSequenceDetector.cs` | F | 20 | Detects stop strings straddling token boundaries. Keeps a rolling tail of `maxStopLen-1` chars. Without it the NPC says `<\|im_end\|>` aloud |
| 13 | `NpcServiceLocator.cs` | A | 26 | Process-wide ref-counted owner of LLM/STT/TTS/VAD and the worker threads. **Hardest correctness problem in the package.** Lazy load, refcount, idle unload, `[RuntimeInitializeOnLoadMethod]` reset, `AssemblyReloadEvents.beforeAssemblyReload` disposal |
| 14 | `NpcPipeline.cs` | A | 25 | The orchestrator. Drives the state machine, wires stage→stage, owns the per-turn CTS, timestamps diagnostics. **No inference code, no vendor types** |
| 15 | `NpcAgent.cs` | F/A | 25 | The one component buyers touch. §7.1. Cancels on `OnDisable`, releases its session ref on `OnDestroy`, never creates threads itself |
| 16 | `NpcInterruptController.cs` | A | 32 | Barge-in: `Ignore` / `StopAndListen` / `StopAfterSentence`. **Echo gating required** — gate VAD on `AudioSource.isPlaying` + 150 ms tail, or the NPC retriggers on its own voice |
| 17 | `NpcDiagnostics.cs` | A | 24 | Per-turn timings (§16.1 names), tok/s, RAM. Ring buffer of 50 turns. Zero cost when disabled — guard with a bool, not `#if` |

## 6.2 Runtime/Audio — 9

| # | File | Own | Step | Purpose & notes |
|---|---|---|---|---|
| 18 | `AudioRingBuffer.cs` | A | 8 | Lock-free SPSC float ring. Power-of-two capacity, mask not modulo, `Volatile.Read/Write` on indices, **no lock, no allocation**. Overflow drops oldest + increments `DroppedFrames` (a rising count means the inference thread is starved) |
| 19 | `AudioResampler.cs` | A | 9 | 48 k / 44.1 k → 16 k mono. Polyphase FIR or windowed-sinc, filter table precomputed once. **Caller supplies the output buffer.** 44.1 k→16 k is non-integer — get it right or Whisper accuracy degrades in a way that looks like a model bug |
| 20 | `AudioPreprocessor.cs` | A | 9 | High-pass ~40 Hz for DC offset, RMS normalise to ~−20 dBFS, optional soft noise gate. **Do not hard-clip** |
| 21 | `MicrophoneCapture.cs` | A | 10 | Device enumeration, `Microphone.Start` loop clip, `GetPosition` read with wraparound, device-lost recovery, hot-swap on default-device change. **Main thread only** — Unity's `Microphone` API requires it |
| 22 | `VoiceActivityGate.cs` | A | 12 | Probabilities → utterance boundaries. `speechThreshold` 0.5, `minSpeechMs` 250, `minSilenceMs` 200, `preRollMs` 300, `maxUtteranceMs` 30000. **Pre-roll is essential** — without it the first phoneme is clipped and Whisper mis-transcribes the first word. Expose every parameter |
| 23 | `SpeechAudioQueue.cs` | A | 23 | FIFO of sentence clips, gap-free through one `AudioSource`. **Use `PlayScheduled` with `AudioSettings.dspTime`** — polling `isPlaying` produces audible seams. `Flush()` stops within one frame |
| 24 | `AudioClipBuilder.cs` | A | 23 | `float[]` → `AudioClip` on main thread, pooled by 0.5 s length buckets, reused via `SetData` |
| 25 | `PushToTalkController.cs` | A | 33 | Key/button gating instead of VAD. Must work with both input systems. Small script, high value for kiosk and for testing without VAD noise |
| 26 | `WavUtility.cs` | A | 8 | Debug-only 16-bit PCM WAV read/write for fixtures and repro clips |

## 6.3 Runtime/Inference/Vad — 3

| # | File | Own | Step | Purpose & notes |
|---|---|---|---|---|
| 27 | `VadModelAsset.cs` | A | 11 | ScriptableObject wrapping the Silero `ModelAsset` + parameters |
| 28 | `SileroVadModel.cs` | A | 11 | Silero v5 via Inference Engine. **Recurrent — hidden state tensors persist between calls and reset on utterance end.** Fixed 512-sample frames @16 k. **Allocate tensors once and reuse**; per-frame allocation at 31 fps is a steady GC drip |
| 29 | `EnergyVadModel.cs` | A | 11 | RMS-threshold fallback. Used when Silero fails to load, and to isolate whether a bug is in VAD or downstream |

## 6.4 Runtime/Inference/Stt — 7
*If Step 3's spike picks whisper.cpp, files 31–34 collapse into one `WhisperCppRecognizer.cs`. `ISpeechRecognizer` is identical either way.*

| # | File | Own | Step | Purpose & notes |
|---|---|---|---|---|
| 30 | `LogMelSpectrogram.cs` | A | 13 | STFT (n_fft 400, hop 160) + 80-bin mel, log-scaled and clamped, matching Whisper's reference **within 1e-3**. Off-by-one in the filterbank silently degrades accuracy and looks like a model problem |
| 31 | `WhisperVocabularyAsset.cs` | A | 14 | Vocab + merges as a `TextAsset`, parsed once into a dictionary |
| 32 | `WhisperTokenizer.cs` | A | 14 | Byte-level BPE detokenisation. **Decode to bytes then UTF-8** — do not concatenate strings per token |
| 33 | `WhisperEncoder.cs` | A | 15 | Encoder execution, output tensor cached for the decode loop |
| 34 | `WhisperDecoder.cs` | A | 15 | Greedy decode with KV cache. Suppress-tokens, no-timestamps, max-length guard, no-speech early-out. **Initial token sequence built from `LanguageProfile.whisperLanguageCode`, never hardcoded `<\|en\|>`**. Check cancellation every step |
| 35 | `WhisperRecognizer.cs` | A | 15 | Implements `ISpeechRecognizer`. Pads to 30 s (Whisper requires it) without letting padding produce hallucinations |
| 36 | `SttPostProcessor.cs` | F | 16 | Removes known hallucinated tails ("Thank you for watching", "Subtitles by…"), repeated n-grams, filler. **Rejects** transcripts under `minCharacters` or `noSpeechProb > 0.6` → back to `Idle`. **Without this the NPC answers questions nobody asked** — the most common complaint in Whisper products |

## 6.5 Runtime/Inference/Llm — 8

| # | File | Own | Step | Purpose & notes |
|---|---|---|---|---|
| 37 | `LlamaNative.cs` | A | 17 | Every `DllImport` in one file, matching §8. `CallingConvention.Cdecl`, **manual UTF-8 marshalling** — Unity's default mangles non-ASCII. If the spike picks a prebuilt library, this wraps *their* API and nothing else changes |
| 38 | `LlamaModelHandle.cs` | A | 17 | `SafeHandle`, ref-counted. **Must release on domain reload.** Leaking 400 MB across a Play-mode restart is the #1 Editor-crash cause in this project class |
| 39 | `LlamaContextHandle.cs` | A | 17 | `SafeHandle` for the per-session context; owns the KV cache |
| 40 | `LlamaSamplingSettings.cs` | F | 17 | Serializable: temperature 0.7, topK 40, topP 0.9, repeatPenalty 1.1, repeatLastN 64, seed −1, maxTokens 200 |
| 41 | `LlamaWorkerThread.cs` | A | 17 | Dedicated thread + `BlockingCollection<WorkItem>`. All native calls here, serialised. **One thread per process, not per agent.** Name it `LocalNPC-Inference` so it's visible in the Profiler |
| 42 | `LlamaTokenStream.cs` | A | 17 | Token ids → UTF-8-safe string deltas. **A single token can be a partial UTF-8 sequence** — buffer bytes, emit only on complete codepoints. Test with emoji and accented characters |
| 43 | `LlamaSession.cs` | A | 17 | Per-conversation context: sequence id, `CachedPrefixTokens`, `Reset()`, `TrimToTokens(n)`. `CachedPrefixTokens` is how prefix caching works — compare leading tokens, eval only the divergent tail |
| 44 | `LlamaBackend.cs` | F/A | 17 | Implements `ILanguageModel`. Everything else in this folder is `internal` |

## 6.6 Runtime/Inference/Tts — 8

| # | File | Own | Step | Purpose & notes |
|---|---|---|---|---|
| 45 | `MarkupStripper.cs` | F | 21 | **Language-universal.** Strips `**bold**`, `_italic_`, list markers, code fences, emoji. The LLM emits markdown in every language — this must not be duplicated per language |
| 46 | `EnglishTextNormalizer.cs` | F | 21 | Implements `ITextNormalizer`. Numbers→words (cardinal, ordinal, years, decimals), currency, `%`, abbreviations. Calls `MarkupStripper` first |
| 47 | `PhonemeMap.cs` | F | 22 | ARPAbet/IPA → voice-specific phoneme ids, loaded from the voice's JSON config. **Never hardcode a phoneme id** |
| 48 | `EnglishLetterToSoundRules.cs` | F | 22 | OOV fallback. Game NPCs use invented names constantly — this path runs more than you'd expect |
| 49 | `CmudictPhonemizer.cs` | F | 22 | Implements `IPhonemizer`. CMUdict lookup (~125 k entries, compressed `TextAsset`), falls back to `EnglishLetterToSoundRules` **internally**. The rules class is not a separate `IPhonemizer` — OOV is an implementation detail, not a pipeline concern |
| 50 | `PiperVoiceAsset.cs` | F | 22 | ScriptableObject: `ModelAsset` + config JSON + **licence name + URL** (mandatory, shown in inspector) + preview clip |
| 51 | `PiperSynthesizer.cs` | A | 22 | Implements `ISpeechSynthesizer`. phoneme ids → VITS → `float[]` PCM @22050. **Depends only on `ITextNormalizer` and `IPhonemizer`, never concrete types.** Output float in [−1,1]; don't resample, `AudioClip.Create` takes 22050 |
| 52 | `TtsCache.cs` | A | 31 | LRU keyed by `hash(sentence + voiceId + settings)`. Cap by total bytes (32 MB default), not entry count |

## 6.7 Runtime/LipSync — 5

| # | File | Own | Step | Purpose & notes |
|---|---|---|---|---|
| 53 | `VisemeFrame.cs` | A | 35 | `readonly struct`: viseme index, weight, timestamp |
| 54 | `IVisemeDriver.cs` | A | 35 | Contract for producing viseme weights |
| 55 | `AmplitudeLipSync.cs` | A | 35 | Drives one jaw-open blendshape or bone from `AudioSource.GetOutputData` RMS. **This is what makes "drag a prefab on" true** for buyers who never rigged visemes. Ship it as the default |
| 56 | `BlendShapeVisemeMapper.cs` | A/R | 35 | Viseme index → blendshape index, smoothing ~0.15 s lerp, max-weight clamp. ARKit-52 and 15-viseme sets. **`SetBlendShapeWeight` is main-thread only and takes 0–100, not 0–1** |
| 57 | `ULipSyncBridge.cs` | A | 35 | Entirely inside `#if LOCALNPC_ULIPSYNC`. Never bundled |

## 6.8 Runtime/Models — 6

| # | File | Own | Step | Purpose & notes |
|---|---|---|---|---|
| 58 | `ModelEntry.cs` | A | 37 | id, displayName, url, mirrorUrl, sha256, sizeBytes, licenceName, licenceUrl, minRamMb, presetTag |
| 59 | `ModelCatalog.cs` | A | 37 | ScriptableObject list of entries incl. the three presets. Editable so buyers can self-host |
| 60 | `ModelStorage.cs` | A | 37 | `persistentDataPath/LocalNPC/models` for downloads, `StreamingAssets/LocalNPC` for pre-bundled. **Keep the extraction path behind a method** so Phase 2 can add the Android APK case without touching callers |
| 61 | `ModelDownloader.cs` | A | 38 | Progress, resume via HTTP `Range`, retry with backoff, mirror failover, cancellation, **free-disk precheck**. Write to `<file>.part`, rename on success — a half-file with the right name is worse than no file |
| 62 | `ModelVerifier.cs` | A | 38 | Streaming SHA-256; mismatch → delete, re-fetch once, then fail clearly. **Never load an unverified binary blob** |
| 63 | `ModelProvisioner.cs` | F | 38 | The single entry point: `EnsureModelsAsync(IProgress<ProvisionStatus>, ct)` |

## 6.9 Runtime/Platform — 3

| # | File | Own | Step | Purpose & notes |
|---|---|---|---|---|
| 64 | `ThreadDispatcher.cs` | A | 6 | `ConcurrentQueue<Action>` drained in `Update()` by a hidden `DontDestroyOnLoad` component created via `[RuntimeInitializeOnLoadMethod]`, `[DefaultExecutionOrder(-1000)]`. **Cap drains per frame (32)** so a flood can't spike frame time |
| 65 | `NativeLibraryLoader.cs` | A | 17 | Verifies the library loads **before** the first P/Invoke; checks `lnpc_abi_version()`. Produce a readable error naming the missing file and the fix — not a bare `DllNotFoundException`. Top-5 support-ticket source in every native package |
| 66 | `PlatformCapabilities.cs` | A | 27 | RAM, physical vs logical cores, CPU family → recommends preset, thread count, context. **The only file that knows about hardware.** Default `threadCount = min(physicalCores − 2, 6)`, floor 2 |

## 6.10 Editor — 10

| # | File | Own | Step | Purpose & notes |
|---|---|---|---|---|
| 67 | `EnvironmentCheckWindow.cs` | A | 4 | The §3.5 check. Build this early — you'll use it constantly |
| 68 | `SetupWizard.cs` | A | 39 | First-run: Unity version, Inference Engine version, unsafe code, audio settings, RAM; offers fixes; then model download |
| 69 | `ModelManagerWindow.cs` | A | 39 | Catalogue list, installed state, size, licence, Download / Delete / Copy-to-StreamingAssets |
| 70 | `DiagnosticsWindow.cs` | A | 40 | Live latency breakdown, transcript log, tok/s, RAM. **Also your primary debugging tool** — build it well |
| 71 | `NpcAgentInspector.cs` | F | 41 | Status badge, download button, live state, and **a "Test with typed input" field** so you can run a full turn with no microphone. Highest-value Editor feature in the package |
| 72 | `NpcPersonalityInspector.cs` | F | 41 | Live system-prompt token count (warns > 200), few-shot list, preview voice, **language dropdown populated from all `LanguageProfile` assets** |
| 73 | `NpcRuntimeConfigInspector.cs` | F | 41 | Preset picker with RAM warning; thread auto/manual |
| 74 | `VisemeMapperInspector.cs` | A | 41 | Blendshape picker with live preview sliders; auto-detects ARKit naming |
| 75 | `BuildPreprocessor.cs` | A | 42 | Validates plugin import settings, verifies models present for StreamingAssets mode, **fails the build with a clear message** rather than shipping something broken |
| 76 | `BuildPostprocessor.cs` | A | 42 | Copies the DLL next to the executable, writes a build manifest (versions, model ids, hashes) for support triage |
| 77 | `PackageExporter.cs` | F | 46 | Stage `package/`, strip `_Dev`, validate, emit `.unitypackage` + UPM tarball |

*(Editor is 11 entries; `EnvironmentCheckWindow` folds into `SetupWizard` at Step 39, netting 10 shipped files.)*

## 6.11 Tests — 13

| # | File | Type | Own | Step | Covers |
|---|---|---|---|---|---|
| 78 | `ThreadDispatcherTests.cs` | Edit | A | 6 | Ordering, per-frame cap, thread safety |
| 79 | `AudioPipelineTests.cs` | Edit | A | 9 | Resampler vs golden fixture (48 k and 44.1 k), ring wraparound and overflow, **zero-alloc assertion** |
| 80 | `VadGateTests.cs` | Edit | A | 12 | Boundaries against all 20 labelled clips |
| 81 | `MelSpectrogramTests.cs` | Edit | A | 13 | Mel vs golden tensors within 1e-3 |
| 82 | `TokenizerTests.cs` | Edit | A | 14 | Detokenisation; **UTF-8 across token boundaries** (emoji, accents) |
| 83 | `SentenceSplitterTests.cs` | Edit | F | 20 | 40+ English cases **plus** two language-neutrality tests (§9 Step 20) |
| 84 | `PromptBuilderTests.cs` | Edit | F | 19 | Template correctness, pair-wise trimming, **prefix stability across 10 turns** |
| 85 | `StopSequenceTests.cs` | Edit | F | 20 | Stop strings split across token boundaries |
| 86 | `ModelProvisionerTests.cs` | Edit | A | 38 | Hash mismatch, resume, disk full, cancellation, mirror failover |
| 87 | `NativeSmokeTests.cs` | Play | A | 17 | Load → 5 tokens → unload. **First test in CI** |
| 88 | `PipelineIntegrationTests.cs` | Play | A | 25 | WAV in → transcript → reply → PCM out; includes the 4-agent multi-agent case |
| 89 | `LatencyBenchmarkTests.cs` | Play | F | 30 | Asserts §16.1 budgets; writes a CSV artefact per run |
| 90 | `DomainReloadTests.cs` | Play | A | 34 | 20 enter/exit cycles mid-generation, no leaked native handles |

## 6.12 Samples — 2 scripts

| # | File | Own | Step | Purpose |
|---|---|---|---|---|
| 91 | `DemoSceneController.cs` | F | 43 | Wires agent, UI, input; first-run provisioning UI |
| 92 | `SubtitleHud.cs` | F | 43 | Streams transcript and reply on screen; state indicator |

Sample scenes (content, not scripts): `01_QuickStart`, `02_Shopkeeper`, `03_Kiosk`.

**Phase 2 adds 5 files, changing nothing above:** `AndroidPermissions.cs`, `AndroidModelExtractor.cs`, `QuestPerformanceProfile.cs`, `XrPushToTalkAdapter.cs`, `VrSampleController.cs`.

---

# PART 7 — Contracts

## 7.1 Public API — what buyers see. Frozen at Step 44.

```csharp
namespace LocalNPC
{
    public class NpcAgent : MonoBehaviour
    {
        public NpcPersonality Personality { get; set; }
        public NpcState       State       { get; }
        public bool           IsReady     { get; }

        public Task PrewarmAsync(CancellationToken ct = default);
        public void StartListening();
        public void StopListening();
        public Task SayAsync(string userText, CancellationToken ct = default);
        public void Interrupt();
        public void ResetConversation();
        public void SetPersonality(NpcPersonality p, bool resetHistory = true);

        public event Action<NpcState, NpcState> OnStateChanged;
        public event Action<string>             OnUserTranscript;
        public event Action<string>             OnReplyChunk;
        public event Action<string>             OnSentenceSpoken;
        public event Action<string>             OnReplyComplete;
        public event Action<NpcError>           OnError;
    }

    public static class LocalNpc
    {
        public static Task EnsureModelsAsync(IProgress<ProvisionStatus> p, CancellationToken ct);
        public static bool AreModelsReady { get; }
        public static void UnloadAll();
        public static LocalNpcSettings Settings { get; }
    }

    public enum NpcErrorCode
    {
        None, ModelMissing, ModelCorrupt, NativeLibraryMissing, AbiMismatch,
        MicrophoneUnavailable, MicrophonePermissionDenied, OutOfMemory,
        InferenceFailed, SynthesisFailed, LanguageUnsupported, Cancelled, Unknown
    }
}
```

**Rules:** every async method takes a `CancellationToken` · no `async void` except Unity handlers · **no exceptions cross the public boundary at runtime** — errors arrive via `OnError` · no vendor names · no platform names (Phase 2 must not force a rename).

## 7.2 Stage contracts — `INpcModules.cs`, written at Step 5

```csharp
namespace LocalNPC.Inference
{
    public interface IModelResource : IDisposable
    {
        bool IsLoaded { get; }
        Task LoadAsync(CancellationToken ct);
        void Unload();
        long EstimatedBytes { get; }
    }

    public interface IVoiceActivityDetector : IModelResource
    {
        int   FrameSamples { get; }                     // 512 for Silero @16k
        float Process(ReadOnlySpan<float> frame);       // returns p(speech)
        void  ResetState();
    }

    public readonly struct SttResult
    {
        public readonly string Text;
        public readonly float  NoSpeechProbability;
    }

    public interface ISpeechRecognizer : IModelResource
    {
        Task<SttResult> TranscribeAsync(float[] pcm16k, string languageCode, CancellationToken ct);
    }

    public interface ILlmSession : IDisposable
    {
        int  CachedPrefixTokens { get; }
        Task GenerateAsync(int[] promptTokens, LlamaSamplingSettings settings,
                           IProgress<string> onDelta, CancellationToken ct);
        void Reset();
        void TrimToTokens(int n);
    }

    public interface ILanguageModel : IModelResource
    {
        ILlmSession CreateSession(int contextTokens);
        int[]  Tokenize(string text);
        string Detokenize(int[] tokens);
    }

    public interface ISpeechSynthesizer : IModelResource
    {
        int SampleRate { get; }
        Task<float[]> SynthesizeAsync(string sentence, CancellationToken ct);
    }
}

namespace LocalNPC.Inference.Tts
{
    /// PUBLIC API. Convert LLM output into speakable text.
    /// Deterministic, allocation-light. Buyers may implement this.
    public interface ITextNormalizer { string Normalize(string sentence); }

    /// PUBLIC API. Convert a normalized sentence into voice-specific phoneme ids.
    /// Implementations handle their own out-of-vocabulary fallback internally.
    /// Buyers and third parties may implement this — see the note below.
    public interface IPhonemizer { int[] Phonemize(string sentence); }
}
```

Note `TranscribeAsync` takes a `languageCode` — that parameter exists from day one so multilingual Whisper needs no signature change.

## 7.3 `LanguageProfile` — the multilingual seam, Step 7

```csharp
namespace LocalNPC
{
    [CreateAssetMenu(menuName = "LocalNPC/Language Profile", fileName = "LanguageProfile")]
    public class LanguageProfile : ScriptableObject
    {
        [Tooltip("BCP-47 tag, e.g. en-US. Must be unique across profiles.")]
        public string languageTag = "en-US";

        [Header("Sentence segmentation")]
        public string sentenceTerminators = ".!?";
        public string trailingClosers     = "\"')]}\u2019\u201d";
        public string[] abbreviations =
            { "Mr.", "Mrs.", "Ms.", "Dr.", "Prof.", "St.", "vs.", "e.g.", "i.e.", "etc.", "Jr.", "Sr." };

        [Header("Implementations")]
        public TextNormalizerKind normalizer = TextNormalizerKind.English;
        public PhonemizerKind     phonemizer = PhonemizerKind.CmudictEnglish;

        [Header("Speech recognition")]
        [Tooltip("Bare Whisper language code, e.g. 'en'. Empty = auto-detect.")]
        public string whisperLanguageCode = "en";

        [Header("Synthesis")]
        public PiperVoiceAsset defaultVoice;

        internal ITextNormalizer CreateNormalizer() => normalizer switch {
            TextNormalizerKind.English => new EnglishTextNormalizer(),
            _ => throw new NotSupportedException($"Normalizer {normalizer} not implemented")
        };

        internal IPhonemizer CreatePhonemizer(PhonemeMap map) => phonemizer switch {
            PhonemizerKind.CmudictEnglish => new CmudictPhonemizer(map),
            _ => throw new NotSupportedException($"Phonemizer {phonemizer} not implemented")
        };
    }

    public enum TextNormalizerKind { English }
    public enum PhonemizerKind     { CmudictEnglish }
}
```

**Why enums, not `SerializeReference`:** Unity's polymorphic serialization has version-tolerance problems across package updates, and a buyer's asset breaking on upgrade is a support nightmare. A factory switching on an enum is boring and survives.

**Adding a language later = one enum value + one implementation class + one asset.** No consumer changes.

### 7.4 Why `IPhonemizer` and `ITextNormalizer` are PUBLIC

`LanguageProfile` also accepts an externally-supplied implementation:

```csharp
[Header("Custom implementations (optional)")]
[Tooltip("Assembly-qualified type name implementing IPhonemizer. Overrides the enum when set.")]
public string customPhonemizerType;

[Tooltip("Assembly-qualified type name implementing ITextNormalizer. Overrides the enum when set.")]
public string customNormalizerType;
```

Resolved by reflection at load, with a clear error if the type is missing or doesn't implement the interface.

**This is the route that unlocks every language.** Our TTS avoids espeak-ng because espeak-ng is GPL-3.0 and cannot ship inside a paid closed-source package. But with a public interface, a **separately distributed** GPL-licensed adapter — written by us as a free open-source side repo, or by anyone in the community — can implement `IPhonemizer` on top of espeak-ng. The buyer installs that add-on themselves.

We never distribute GPL code inside the paid package. The adapter is its own work under its own licence. If this holds, it opens **every language espeak-ng supports — 100+, Bangla included.**

**This is a licence-boundary question and it needs qualified legal review before we rely on it.** Spike E (Step 3B) gathers the facts; Fahim gets the legal answer before anything is promised publicly. Build the seam regardless — a public interface is good design on its own merits.

---

# PART 8 — Native plugin ABI

Whether Step 2's spike picks a prebuilt library or we build our own, C# talks to exactly this.

```c
/* lifecycle */
void*       lnpc_backend_init(const char* params_json);
void        lnpc_backend_free(void* backend);

/* model */
void*       lnpc_model_load(void* backend, const char* gguf_path, const char* params_json);
void        lnpc_model_free(void* model);

/* context / session */
void*       lnpc_ctx_create(void* model, int n_ctx, int n_threads);
void        lnpc_ctx_free(void* ctx);
void        lnpc_ctx_reset(void* ctx);

/* tokenization */
int         lnpc_tokenize(void* model, const char* text, int* out_tokens, int max_tokens);
int         lnpc_detokenize(void* model, const int* tokens, int n, char* out_utf8, int max_bytes);

/* generation — blocking on the caller's worker thread; cancel from any thread */
int         lnpc_eval_prompt(void* ctx, const int* tokens, int n_tokens);
int         lnpc_sample_next(void* ctx, const char* sampling_json);   /* token id, −1 = EOS */
void        lnpc_request_cancel(void* ctx);

/* introspection */
int         lnpc_ctx_used_tokens(void* ctx);
const char* lnpc_last_error(void);
int         lnpc_abi_version(void);
```

**Constraints:**
- Only primitives and UTF-8 `char*` cross the boundary. No structs, no C++ types.
- **No callbacks from native into C#.** A delegate invoked on a non-Unity thread is a known crash source. We poll `lnpc_sample_next` from our own worker instead.
- Every pointer wrapped in a `SafeHandle`.
- `lnpc_abi_version()` checked at load; mismatch → clear error, not a crash.
- Native never writes to stdout in release.
- String returns are owned by the native side and valid until the next call on that thread — **copy immediately**.
---

# PART 9 — Step-by-step build order

47 steps in 8 stages. Work them in order. Each step: **Goal / Files / How / Acceptance / Common mistakes.**

A step is **done when Acceptance passes**, not when it compiles.

---

## STAGE A — Spikes and foundations (Steps 1–8)

Do not build the pipeline here. Every hour of architecture written before the spikes close may be discarded.

---

### Step 1 — Repo, project, assemblies
**Own:** A · **Est:** 6 h

**Goal:** a clean Unity project everyone can clone and open identically.

**Files:** `.gitattributes`, `unity/` project, 5 asmdefs, folder skeleton per §5.3.

**How**
1. `git init`, add `.gitattributes` from §5.2, `git lfs install`
2. Create the Unity 6 LTS project in `unity/`, commit `ProjectSettings/`
3. Pin the Inference Engine version in `Packages/manifest.json`
4. Create the folder skeleton and all 5 asmdefs with the references in §5.4
5. Apply every setting in §3.4 and commit `ProjectSettings/`

**Acceptance**
- A fresh clone on a second machine opens with zero errors and zero warnings
- All 5 assemblies compile empty
- `git lfs pull` materialises real files

**Common mistakes:** forgetting to commit `ProjectSettings/` (everyone gets different settings); leaving the Inference Engine version floating.

---

### Step 2 — Spike A: native LLM backend
**Own:** F (you assist) · **Est:** 6 h

**Goal:** decide whether we wrap a prebuilt llama.cpp library or build our own DLL.

**How:** evaluate LLMUnity / LlamaLib. Confirm (a) the licence permits redistribution inside a paid closed-source Asset Store package, (b) binary < 20 MB, (c) an arm64 build exists for Phase 2, (d) it loads and generates in Unity 6 within two days of work.

**Acceptance:** a written verdict in §6.5 notes, plus a Unity scene that generates 20 tokens from a GGUF.

**If rejected:** we build a Windows-only `.dll` (`native/CMakeLists.txt`, MSVC x64, `/MD`). Adds ~2 weeks; Fahim pairs with you for that fortnight. Android build is deferred to Phase 2 either way.

---

### Step 3 — Spike B: STT backend, and Spike D: model throughput
**Own:** A · **Est:** 13 h

**Goal (B):** Sentis Whisper vs whisper.cpp. Transcribe a 3-second 16 kHz clip both ways. Record latency, peak RAM, lines of code.

**Goal (D):** measure tok/s and RAM for 0.5B / 1.5B / 3B on your Windows hardware. Validate the §16.2 memory table.

**How:** two throwaway scenes. Do not integrate anything. Write numbers into a comparison table.

**Acceptance:** comparison table delivered; decision recorded in §6.4; §16.2 updated with real numbers.

**Default if inconclusive:** whisper.cpp — it's the same ggml ecosystem as the LLM, often the same binary, which *collapses* a dependency rather than adding one.

---

### Step 3B — Spike E: language feasibility
**Own:** F · **Est:** 8 h · **Runs in parallel with Step 3**

**Goal:** replace guesswork about Bangla and other languages with four measured facts. Until this runs, nobody on the team can honestly answer "can it speak Bangla?"

**How** — four timeboxed checks, two hours each:

| # | Check | Method | What the answer decides |
|---|---|---|---|
| E1 | **Piper voice availability** | Open the Piper voices list. Record whether a Bengali voice exists, its licence, and its quality tiers. Also check Hindi, Spanish, German — that shows how wide the door is generally | Whether TTS is a config change or a training project |
| E2 | **Bangla TTS end-to-end** | Take one Bangla sentence. Phonemize it by any means (including espeak-ng locally — this is a test, not a shipped artifact). Feed the ids to a Bengali Piper voice if one exists. Listen | Whether the TTS path is real at all |
| E3 | **Whisper Bangla accuracy** | Run Whisper `small` multilingual over 10 recordings of your own Bangla speech. Estimate word error rate roughly | Whether voice *input* works, or only typed input |
| E4 | **LLM Bangla quality** | Prompt Qwen2.5-1.5B and 3B in Bangla with three NPC personas. Read the replies | Whether a stock model suffices or a fine-tune is required |

Also, separately: **get a qualified legal read on the §7.4 GPL-adapter route.** That answer matters more than all four checks combined, because it governs every language, not just Bangla.

**Acceptance:** a one-page written verdict with all four results, appended to Part 2 of this document.

**What the outcomes mean**

| Outcome | Implication |
|---|---|
| All four fine | Bangla is a **~120 h feature**, not a 220 h project. Real roadmap item after v1.0 |
| Voice exists, Whisper poor | **Text-input Bangla NPCs work today.** Voice input needs a fine-tuned Whisper. Ship the typed path first |
| No Bengali Piper voice | §7.4 adapter route plus a community voice, or voice training as its own project |
| LLM output poor at 3B | Bangla needs a fine-tuned model regardless of everything else |

**Scope guard:** this spike produces **a document, not code.** No Bangla implementation lands in Stage A.

---

### Step 4 — Environment Check tool
**Own:** A · **Est:** 6 h

**Goal:** a one-click answer to "is my machine set up right?"

**Files:** `EnvironmentCheckWindow.cs` (#67), plus a dev-model fetch script.

**How:** menu item `LocalNPC → Developer → Environment Check`. Assert: Unity version, Inference Engine version, unsafe code enabled, LFS files real (not pointer stubs), native DLL present and loadable, dev models cached. Green/red per row with a fix hint.

**Acceptance:** green on both dev machines. Deliberately break one setting and confirm it goes red with a useful message.

**Why this early:** you'll run it after every environment change for the next six months.

---

### Step 5 — Contracts: interfaces, state machine, events
**Own:** F · **Est:** 10 h

**Files:** `INpcModules.cs` (#1), `NpcState.cs` (#3), `NpcEvents.cs` (#4)

**How:** exactly as written in §7.2. No implementations.

**Acceptance:** compiles; state machine has a passing unit test for legal and illegal transitions.

**Why before everything:** every other file depends on these shapes. Changing them later means changing every consumer.

---

### Step 6 — Logging and thread dispatch
**Own:** A · **Est:** 6 h

**Files:** `LocalNpcLog.cs` (#2), `ThreadDispatcher.cs` (#64), `ThreadDispatcherTests.cs` (#78)

**How**
- `LocalNpcLog`: levels Error/Warn/Info/Verbose, `LOCALNPC_VERBOSE` define for the last two. **Thread-safe** — worker messages go into a concurrent queue and flush on main. Every message prefixed `[LocalNPC]`.
- `ThreadDispatcher`: `ConcurrentQueue<Action>`, drained in `Update()` by a hidden `DontDestroyOnLoad` component created via `[RuntimeInitializeOnLoadMethod]` with `[DefaultExecutionOrder(-1000)]`. Cap drains at 32 per frame.

**Acceptance:** `ThreadDispatcherTests` green — ordering preserved, cap honoured, safe from 4 concurrent producers. A worker-thread `LocalNpcLog.Info` appears in the console without throwing.

**Common mistakes:** calling `Debug.Log` from the worker directly (crashes or silently drops); no per-frame cap (a flood spikes frame time).

---

### Step 7 — `LanguageProfile`
**Own:** A · **Est:** 3 h

**Files:** `LanguageProfile.cs` (#5), `Resources/LanguageProfile_en-US.asset`

**How:** exactly as §7.3. Create the single `en-US` asset. Factory methods throw a readable `NotSupportedException` for unimplemented kinds.

**Acceptance:** asset creatable from the menu; `en-US` instance exists; factories return working English implementations once Steps 21–22 land (stub them until then).

**Scope guard:** create **one** profile. If your PR contains `bn-BD` outside a test fixture, it gets rejected.

---

### Step 8 — Test fixtures and WAV utility
**Own:** A · **Est:** 8 h

**Files:** `WavUtility.cs` (#26), `Tests/Fixtures/`

**How:** record or source 20 labelled 16 kHz mono WAV clips: quiet room, noisy, fast speech, accented, long mid-sentence pause, cough, music behind, silence only, single word, 25-second monologue, and 10 ordinary sentences. Also: golden mel tensors for 3 clips, tokenizer golden pairs including emoji and accented characters, a ~5 MB tiny test GGUF, 100 fantasy names for the G2P fallback test.

**Acceptance:** all committed via LFS, under 60 MB total; `WavUtility` round-trips a clip bit-exact.

**Why now:** every later step's acceptance depends on these. Building them later means writing tests you can't run.

---

**STAGE A GATE:** all spikes have written verdicts. Environment Check green on both machines. **If Spike C (phonemization, Fahim, runs in parallel) finds no permissive path, Stage B does not start — TTS gets re-planned first.**

---

## STAGE B — Audio input (Steps 9–12)

---

### Step 9 — Ring buffer, resampler, preprocessor
**Own:** A · **Est:** 16 h

**Files:** `AudioRingBuffer.cs` (#18), `AudioResampler.cs` (#19), `AudioPreprocessor.cs` (#20), `AudioPipelineTests.cs` (#79)

**How**
- Ring buffer: power-of-two capacity, mask not modulo, `Volatile.Read/Write` on indices, no lock, no allocation. Overflow drops oldest and increments `DroppedFrames`.
- Resampler: polyphase FIR or windowed-sinc, filter table precomputed in the constructor. Signature `int Resample(ReadOnlySpan<float> input, Span<float> output)` — **caller owns the output buffer.**
- Preprocessor: high-pass ~40 Hz, RMS normalise to ~−20 dBFS, optional soft gate. No hard clipping.

**Acceptance:** `AudioPipelineTests` green including the **zero-allocation assertion**; resampler output matches the golden fixture for both 48 k and 44.1 k sources.

**Common mistakes:** allocating a new `float[]` per call (kills the zero-alloc target); getting 44.1 k→16 k wrong because it's a non-integer ratio — this degrades Whisper accuracy in a way that looks like a model bug and costs days to diagnose.

---

### Step 10 — Microphone capture
**Own:** A · **Est:** 8 h

**Files:** `MicrophoneCapture.cs` (#21)

**How:** enumerate devices, `Microphone.Start` with a looping clip, read via `GetPosition` handling wraparound, write into the ring buffer from `Update()`. Poll the device list every 2 seconds to detect unplug. Recover on device loss; hot-swap when the default device changes.

**Acceptance:** captures at both 48 k and 44.1 k; survives unplugging the mic mid-capture and recovers when replugged; no allocation per frame.

**Common mistakes:** calling `Microphone` from a worker thread (it's main-thread only); ignoring wraparound (you get gaps and duplicated audio every buffer cycle).

---

### Step 11 — VAD models
**Own:** A · **Est:** 12 h

**Files:** `VadModelAsset.cs` (#27), `SileroVadModel.cs` (#28), `EnergyVadModel.cs` (#29)

**How:** Silero v5 via Inference Engine. **The model is recurrent** — hidden state tensors must persist between calls and reset on utterance end. Fixed 512-sample frames at 16 kHz. **Allocate input/state tensors once in `LoadAsync` and reuse them** — per-frame tensor allocation at ~31 fps is a steady GC drip that will fail your zero-alloc target.

Also implement `EnergyVadModel` — a trivial RMS threshold. You'll use it constantly to isolate whether a bug is in VAD or downstream.

**Acceptance:** returns sane probabilities across the 20 fixtures (high on speech clips, low on the silence-only clip); zero allocation per frame after warm-up.

---

### Step 12 — Voice activity gate
**Own:** A · **Est:** 10 h

**Files:** `VoiceActivityGate.cs` (#22), `VadGateTests.cs` (#80)

**How:** convert probabilities into utterance boundaries with hysteresis. Parameters: `speechThreshold` 0.5, `minSpeechMs` 250, `minSilenceMs` 200, `preRollMs` 300, `maxUtteranceMs` 30000. **Keep a circular pre-roll buffer always running** and prepend it to every emitted utterance.

**Acceptance:** `VadGateTests` green on all 20 fixtures — correct boundary count, no split on the mid-sentence pause clip, nothing emitted for silence-only.

**Common mistakes:** no pre-roll — the first phoneme gets clipped and Whisper mis-transcribes the first word of every utterance. This is the single most common VAD bug and it looks like an STT problem.

**Note:** this file generates the "it cuts me off" and "it waits too long" complaints. Expose every parameter in the inspector.

---

## STAGE C — Speech to text (Steps 13–16)

---

### Step 13 — Mel spectrogram
**Own:** A · **Est:** 8 h

**Files:** `LogMelSpectrogram.cs` (#30), `MelSpectrogramTests.cs` (#81)

**How:** STFT with n_fft 400, hop 160, Hann window; 80-bin mel filterbank; log-scale and clamp. Match Whisper's reference preprocessing exactly.

**Acceptance:** output matches golden tensors **within 1e-3** for all 3 fixture clips.

**Common mistakes:** wrong window function, off-by-one in the filterbank, wrong log base or clamp. All of these silently degrade transcription accuracy — you'll blame the model for days. **Write this test first.**

---

### Step 14 — Whisper tokenizer
**Own:** A · **Est:** 8 h

**Files:** `WhisperVocabularyAsset.cs` (#31), `WhisperTokenizer.cs` (#32), `TokenizerTests.cs` (#82)

**How:** byte-level BPE. **Decode to a byte buffer, then convert to UTF-8** — do not concatenate strings per token. Handle special tokens.

**Acceptance:** `TokenizerTests` green including UTF-8 across token boundaries (emoji, accented characters).

---

### Step 15 — Whisper inference
**Own:** A · **Est:** 20 h

**Files:** `WhisperEncoder.cs` (#33), `WhisperDecoder.cs` (#34), `WhisperRecognizer.cs` (#35)
*If Step 3 chose whisper.cpp, this collapses into one `WhisperCppRecognizer.cs` — roughly 8 h instead of 20.*

**How:** encoder execution with output cached for the decode loop; greedy decode with KV cache; suppress-tokens list; no-timestamps token; max-length guard; no-speech probability early-out. Pad audio to 30 seconds (Whisper requires it).

**Language neutrality:** the initial token sequence is built from the `languageCode` parameter, not a hardcoded `<|en|>`. Empty string means omit the language token (auto-detect). If the loaded model is English-only and a different code is requested, **log a warning and continue** — never throw.

**Acceptance:** transcribes all 20 fixtures with reasonable accuracy; cancellation stops decoding within one step; a unit test proves the initial token sequence changes when the language code changes.

**Common mistakes:** letting 30-second padding produce hallucinated content (Step 16 handles it, but don't make it worse); not checking the cancellation token in the decode loop.

---

### Step 16 — STT post-processing
**Own:** F · **Est:** 6 h

**Files:** `SttPostProcessor.cs` (#36)

**How:** strip known hallucinated tails ("Thank you for watching", "Subtitles by…", a bare "you"), collapse repeated n-grams, trim filler. **Reject** transcripts under `minCharacters` (default 2) or with `noSpeechProbability > 0.6` — the pipeline returns to `Idle`.

**Acceptance:** the silence-only fixture produces an empty result; the cough fixture produces an empty result; ordinary clips are unchanged.

**Why this matters:** without it, Whisper-tiny hallucinates on short and silent clips and the NPC answers questions the player never asked. This is the single most common complaint in Whisper-based products.

---

## STAGE D — Language model (Steps 17–20)

---

### Step 17 — LLM backend
**Own:** A (F designs the API) · **Est:** 24 h

**Files:** `LlamaNative.cs` (#37), `LlamaModelHandle.cs` (#38), `LlamaContextHandle.cs` (#39), `LlamaSamplingSettings.cs` (#40), `LlamaWorkerThread.cs` (#41), `LlamaTokenStream.cs` (#42), `LlamaSession.cs` (#43), `LlamaBackend.cs` (#44), `NativeLibraryLoader.cs` (#65), `NativeSmokeTests.cs` (#87)

**How**
- `LlamaNative`: every `DllImport` in one file, matching §8. `CallingConvention.Cdecl`, **manual UTF-8 marshalling** — Unity's default marshalling mangles non-ASCII.
- Handles: `SafeHandle` subclasses, ref-counted, releasing on `AssemblyReloadEvents.beforeAssemblyReload`.
- `LlamaWorkerThread`: one thread for the whole process, `BlockingCollection<WorkItem>`, all native calls serialised here. Name it `LocalNPC-Inference`.
- `LlamaTokenStream`: **buffer bytes, emit only complete UTF-8 codepoints.** A single token can be a partial sequence.
- `NativeLibraryLoader`: verify loadable and check `lnpc_abi_version()` **before** the first P/Invoke; produce an error naming the missing file and the fix.

**Acceptance:** `NativeSmokeTests` green — load model, generate 5 tokens, unload, no leak. Emoji test passes. Deleting the DLL produces a readable error, not a `DllNotFoundException` dump.

**Common mistakes:** raw `IntPtr` instead of `SafeHandle` (leaks 400 MB across domain reload and crashes the Editor); concatenating token strings without UTF-8 buffering (garbled output on any non-ASCII); creating a thread per agent.

---

### Step 18 — Conversation state
**Own:** A (personality/config assets: F) · **Est:** 10 h

**Files:** `ConversationTurn.cs` (#8), `ConversationSession.cs` (#9), `NpcPersonality.cs` (#6), `NpcRuntimeConfig.cs` (#7)

**How:** trimming policy — always keep system prompt and few-shot examples; drop **oldest user/assistant pairs**, never a lone turn (a lone turn corrupts the chat template); reserve 256 tokens for the reply.

`NpcPersonality` gets a `language` field holding a BCP-47 tag, defaulting to `en-US`. **Add it now even though only `en-US` is valid** — adding a field to a serialized ScriptableObject after buyers have created assets is a migration problem.

**Acceptance:** trimming never drops the system prompt; never leaves an unpaired turn; token accounting matches the tokenizer within 1 token.

---

### Step 19 — Prompt builder
**Own:** F · **Est:** 10 h

**Files:** `PromptBuilder.cs` (#10), `PromptBuilderTests.cs` (#84)

**How:** render persona + few-shot + history into the model's chat template (ChatML for Qwen). The template is data-driven — prefix/suffix strings per role — so swapping model families is config, not code.

**Critical:** the leading portion (system prompt + few-shot) must be **byte-identical across turns**, or prefix caching stops working and prefill latency triples.

**Acceptance:** `PromptBuilderTests` green including the **prefix-stability test** across 10 turns.

**Common mistakes:** including a timestamp, a turn counter, or anything varying in the prefix. Latency will silently double and you'll spend a day looking in the wrong place.

---

### Step 20 — Sentence splitter and stop detection
**Own:** F · **Est:** 12 h · **Test-first**

**Files:** `SentenceSplitter.cs` (#11), `StopSequenceDetector.cs` (#12), `SentenceSplitterTests.cs` (#83), `StopSequenceTests.cs` (#85)

**How** — `SentenceSplitter` takes a `LanguageProfile` in its constructor and reads terminators, closers, and abbreviations from it. **No literal `'.'`, `'!'`, `'?'` anywhere in the file.**
- Build `HashSet<char>` of terminators and closers once in the constructor
- Abbreviation check: on a terminator, look back at the current word against the profile list
- Decimal guard: terminator with digits both sides is not a break
- Ellipsis: a run of terminators is one break
- Consume any run of `trailingClosers` after a terminator before breaking
- `maxCharsBeforeFlush` (220) forces a break on run-ons
- `completedOut` is a caller-supplied `List<string>` — **no allocation per call**

`StopSequenceDetector` keeps a rolling tail of `maxStopLength − 1` characters and never emits text that could still become part of a stop string.

**Acceptance**
- 40+ English cases green
- **Language-neutrality test 1:** a synthetic profile with terminators `"।"` splits a two-sentence string correctly
- **Language-neutrality test 2:** a profile with an empty abbreviation list splits `"Dr. Smith"` into two — proving the list is read, not hardcoded
- Zero allocation in `Push`

**Note:** the two neutrality tests use a non-English character **as a fixture only.** That is not Bangla support.

---

## STAGE E — Speech synthesis (Steps 21–23)

---

### Step 21 — Text normalisation
**Own:** F · **Est:** 10 h

**Files:** `MarkupStripper.cs` (#45), `EnglishTextNormalizer.cs` (#46)

**How:** two layers, deliberately separated.

| Layer | Language-dependent? | Where |
|---|---|---|
| Strip `**bold**`, `_italic_`, list markers, code fences, emoji | **No** | `MarkupStripper`, static, called by every normalizer |
| Numbers→words, currency, ordinals, abbreviation expansion | **Yes** | `EnglishTextNormalizer` |

**This split is the point.** The LLM emits markdown in every language; duplicating that logic per language guarantees drift.

**Acceptance:** `**Hello**, that'll be $3.50.` normalises to speakable text with no asterisks and "three dollars fifty". `MarkupStripper` has its own tests and contains no language-specific logic.

---

### Step 22 — Phonemization and synthesis
**Own:** F (phonemizer) / A (synthesizer) · **Est:** 20 h

**Files:** `PhonemeMap.cs` (#47), `EnglishLetterToSoundRules.cs` (#48), `CmudictPhonemizer.cs` (#49), `PiperVoiceAsset.cs` (#50), `PiperSynthesizer.cs` (#51)

**How**
- `CmudictPhonemizer` implements `IPhonemizer`, owns the CMUdict lookup (~125 k entries, compressed `TextAsset`), and falls back to `EnglishLetterToSoundRules` **internally**. The rules class is not a separate `IPhonemizer` — OOV handling is an implementation detail, not a pipeline concern.
- `PhonemeMap` loads ids from the **voice's JSON config**. Never hardcode a phoneme id.
- `PiperVoiceAsset` carries mandatory licence name and URL fields.
- `PiperSynthesizer` depends only on `ITextNormalizer` and `IPhonemizer`, obtained from the `LanguageProfile` factories.

**Acceptance:** synthesizes a sentence to PCM in under 150 ms; the 100-fantasy-name fixture produces plausible phonemes with no crashes; `PiperSynthesizer` contains no reference to a concrete normalizer or phonemizer type.

---

### Step 23 — Audio output
**Own:** A · **Est:** 10 h

**Files:** `SpeechAudioQueue.cs` (#23), `AudioClipBuilder.cs` (#24)

**How:** `AudioClipBuilder` converts `float[]` → `AudioClip` on the main thread, pooling by 0.5-second length buckets and reusing via `SetData`. `SpeechAudioQueue` plays queued clips gap-free through one `AudioSource` using **`PlayScheduled` with `AudioSettings.dspTime`**.

**Acceptance:** five consecutive sentences play with no audible seam; `Flush()` stops within one frame; no `AudioClip` allocation after warm-up.

**Common mistakes:** polling `isPlaying` and calling `Play()` — produces a small but clearly audible gap between every sentence, and it's the difference between "a character talking" and "a computer reading sentences."

---

## STAGE F — Orchestration (Steps 24–28)

---

### Step 24 — Diagnostics
**Own:** A · **Est:** 6 h

**Files:** `NpcDiagnostics.cs` (#17)

**How:** per-turn record with the timestamp fields named in §16.1, plus tok/s and peak RAM. Ring buffer of the last 50 turns. Zero cost when disabled — guard with a bool check, not `#if`, so benchmarks can enable it at runtime.

**Acceptance:** all §16.1 timestamps populate during a manual turn.

**Why before the pipeline:** you cannot optimise what you cannot measure, and you will be optimising from Step 29 onward.

---

### Step 25 — The pipeline and the agent
**Own:** A (F designs `NpcAgent`'s API) · **Est:** 18 h

**Files:** `NpcPipeline.cs` (#14), `NpcAgent.cs` (#15), `PipelineIntegrationTests.cs` (#88)

**How:** `NpcPipeline` drives the state machine, wires stage output to next stage input, owns the per-turn `CancellationTokenSource`, and writes diagnostics timestamps. **It contains no inference code and references no vendor type — only the interfaces from §7.2.**

`NpcAgent` cancels on `OnDisable`, releases its session ref on `OnDestroy`, and never creates threads itself.

**Acceptance:** `PipelineIntegrationTests` green — WAV fixture in → transcript → reply → PCM out, with the mic faked.

---

### Step 26 — Shared resources
**Own:** A · **Est:** 12 h

**Files:** `NpcServiceLocator.cs` (#13)

**How:** process-wide, ref-counted ownership of the LLM, STT, TTS, VAD instances and the two worker threads. Lazy load on first agent; ref-count on acquire/release; unload after `idleUnloadSeconds` with no refs; reset via `[RuntimeInitializeOnLoadMethod]`; dispose on `AssemblyReloadEvents.beforeAssemblyReload`.

**Acceptance:** four agents in one scene share one model — verify by watching RAM, which must not grow by 400 MB per agent.

**This is the hardest correctness problem in the package.** Get it wrong and you get the Editor-crash bug class. Ask for a pairing session if it's fighting you.

---

### Step 27 — Platform capabilities
**Own:** A · **Est:** 6 h

**Files:** `PlatformCapabilities.cs` (#66)

**How:** detect total RAM, physical vs logical cores, CPU family. Recommend preset, thread count, context length. Default `threadCount = min(physicalCores − 2, 6)`, floor 2 — leave headroom for the render thread.

**Acceptance:** returns sane values on both dev machines; `threadCount = 0` in config resolves through this class.

**Note:** this is the only file in the package that knows about hardware. Phase 2 extends it; nothing else changes.

---

### Step 28 — Grey-box scene
**Own:** A · **Est:** 6 h

**Goal:** **the vertical slice closes here.** A capsule in an empty scene. You speak. It speaks back.

**Files:** `_Dev/GreyBox.unity`, a temporary subtitle display

**Acceptance**
- End-of-speech → first audio measured and logged, **≤ 1800 ms** (not yet the 1000 ms target)
- Works in the Editor **and** in a standalone Mono build
- Record a 60-second unscripted conversation and share it with the team

**This is the most important milestone in the project.** Everything before it is plumbing; everything after it is quality.

---

## STAGE G — Hardening (Steps 29–36)

---

### Step 29 — Profiling pass
**Own:** A · **Est:** 14 h

**Goal:** main thread < 1.5 ms added; **zero GC allocation** in the audio and inference paths.

**How:** Profiler with allocation callstacks enabled, 60-second capture during continuous conversation. Fix every allocation in a per-frame or per-audio-frame path. Usual culprits: tensor allocation in VAD, `float[]` in the resampler, string concatenation in the token stream, `AudioClip` creation without pooling.

**Acceptance:** T2 and T3 green (§16.3).

---

### Step 30 — Prefix caching
**Own:** F · **Est:** 10 h

**Files:** `LlamaSession.cs` (#43) extension, `LatencyBenchmarkTests.cs` (#89)

**How:** on each turn, compare the new prompt's leading tokens against `CachedPrefixTokens` and `eval` only the divergent tail.

**Acceptance:** prefill drops from ~350 ms to under 100 ms; **T1 (< 1000 ms end-to-end) now achievable**; benchmark CSV artefact produced.

**Depends on** Step 19's prefix stability. If the prefix isn't byte-stable, this does nothing and looks like it's working.

---

### Step 31 — TTS cache
**Own:** A · **Est:** 6 h · **Files:** `TtsCache.cs` (#52)

LRU keyed by `hash(sentence + voiceId + settings)`, capped by total bytes (32 MB), not entry count.

**Acceptance:** a repeated greeting synthesizes once; cap honoured under sustained load.

---

### Step 32 — Interrupts and barge-in
**Own:** A · **Est:** 12 h · **Files:** `NpcInterruptController.cs` (#16)

Three modes: `Ignore`, `StopAndListen`, `StopAfterSentence`. In `StopAndListen`, VAD stays armed during `Speaking` — which requires **echo gating**, or the NPC retriggers on its own voice. Simplest correct approach: gate VAD on `AudioSource.isPlaying` plus a 150 ms tail.

**Acceptance:** T7 green — audio stops within 100 ms of `Interrupt()`. The open-mic self-trigger limitation is documented (recommend headphones or push-to-talk).

---

### Step 33 — Push to talk
**Own:** A · **Est:** 5 h · **Files:** `PushToTalkController.cs` (#25)

Works with both legacy Input and the Input System. Small script, high value for kiosk deployments and for testing without VAD noise.

---

### Step 34 — Domain reload safety
**Own:** A · **Est:** 12 h · **Files:** `DomainReloadTests.cs` (#90) + fixes

**How:** enter and exit Play mode 20 times mid-generation. Assert no leaked native handles and no growth in native allocation.

**Acceptance:** T10 green — 20 cycles clean, Editor stable.

**Fix every leak this finds now.** These bugs compound and each one costs you a full Editor restart when it bites.

---

### Step 35 — Lip sync
**Own:** A (with Rakib) · **Est:** 16 h

**Files:** `VisemeFrame.cs` (#53), `IVisemeDriver.cs` (#54), `AmplitudeLipSync.cs` (#55), `BlendShapeVisemeMapper.cs` (#56), `ULipSyncBridge.cs` (#57)

**How:** `AmplitudeLipSync` is the default and the fallback — RMS from `AudioSource.GetOutputData` driving one jaw-open blendshape or bone. `BlendShapeVisemeMapper` handles ARKit-52 and 15-viseme sets with ~0.15 s smoothing. `SetBlendShapeWeight` takes **0–100, not 0–1**, and is main-thread only.

**Acceptance:** mouth moves in time on Rakib's character; the amplitude fallback works on a completely unrigged mesh.

---

### Step 36 — IL2CPP, presets, soak
**Own:** A · **Est:** 18 h

**How:** verify IL2CPP build (stripping level Low; add `link.xml` if reflection breaks). Wire the three model presets end to end with `PlatformCapabilities` recommending by RAM. Run a 30-minute soak with a memory graph.

**Acceptance:** T5, T7, T9, T11 green (§16.3). All three presets load and generate; a warning fires when the selected preset exceeds detected RAM.

---

**STAGE G GATE:** T1–T7 and T9–T11 all green, recorded in the benchmark CSV.

---

## STAGE H — Productisation (Steps 37–47)

---

### Step 37 — Model catalogue and storage
**Own:** A · **Est:** 10 h · **Files:** #58, #59, #60

Catalogue drives all model resolution. Keep the extraction path behind a method so Phase 2 can add the Android APK case without touching callers.

---

### Step 38 — Download and verification
**Own:** A · **Est:** 20 h · **Files:** #61, #62, #63, #86

Progress, resume via HTTP `Range`, retry with backoff, mirror failover, cancellation, free-disk precheck. Write to `<file>.part` and rename on success. Streaming SHA-256; mismatch → delete and re-fetch once, then fail clearly.

**Acceptance:** `ModelProvisionerTests` green including resume, disk-full, and cancellation cases.

---

### Step 39 — Setup wizard and model manager
**Own:** A · **Est:** 16 h · **Files:** #68, #69

`EnvironmentCheckWindow` from Step 4 folds into `SetupWizard` here.

**Acceptance:** fresh project → models installed without leaving the Editor.

---

### Step 40 — Diagnostics window
**Own:** A · **Est:** 10 h · **Files:** #70

Live latency breakdown, transcript log, tok/s, RAM graph. **This becomes your primary debugging tool in Phase 2 — build it well.**

---

### Step 41 — Inspectors
**Own:** F (you build #74) · **Est:** 24 h · **Files:** #71, #72, #73, #74

The **"Test with typed input" field** on `NpcAgentInspector` is the highest-value Editor feature in the package — it lets a developer run a full turn with no microphone.

`NpcPersonalityInspector` renders `language` as a dropdown populated from all `LanguageProfile` assets in the project. With one profile shipping, that dropdown has exactly one entry — which is the correct UX for "this exists but isn't a choice yet."

---

### Step 42 — Build pipeline
**Own:** A · **Est:** 12 h · **Files:** #75, #76

`BuildPreprocessor` fails the build with a clear message rather than shipping something broken. `BuildPostprocessor` copies the DLL and writes a build manifest for support triage.

---

### Step 43 — Sample scenes
**Own:** F · **Est:** 30 h · **Files:** #91, #92 + three scenes

`01_QuickStart` (capsule, minimal UI), `02_Shopkeeper` (Rakib's character, keyword event hooks), `03_Kiosk` (push-to-talk, pre-provisioned models, offline configuration).

---

### Step 44 — API freeze
**Own:** F · **Est:** 6 h

Review §7.1, rename anything awkward, publish as final. After this, changes are breaking and need a changelog entry.

---

### Step 45 — Documentation
**Own:** F · **Est:** 30 h

Ten documents, listed in §17. The **Personality Writing Guide** is the highest-value one — most support tickets will be bad prompts, not bugs.

---

### Step 46 — Package validation and export
**Own:** F (you assist) · **Est:** 12 h · **Files:** #77

Clean import into an empty Unity 6 LTS project: 0 errors, 0 warnings. Plugin import settings `x86_64` only, no stale platform flags. `CHANGELOG.md`, `package.json` at 1.0.0.

---

### Step 47 — Beta and stabilisation
**Own:** A + F · **Est:** 70 h

Beta build, issue tracker, external testers. Full manual matrix (§11.3) on Mono and IL2CPP. Triage and fix every P0/P1. Final 30-minute soak.

**Acceptance:** T12 green — 7 of 10 testers reach a talking NPC in under 15 minutes unaided. If fewer, the defect is in documentation and the inspector, **not the pipeline.**
---

# PART 10 — Coding rules

## 10.1 Style
- C# 9 under .NET Standard 2.1. Prefer `readonly struct`, pattern matching, `Span<T>`
- `PascalCase` types and public members · `camelCase` locals and parameters · `_camelCase` private fields
- Interfaces prefixed `I`
- One public type per file; file name matches the type
- Explicit `private` — don't rely on the default
- `var` only when the type is obvious from the right-hand side

## 10.2 Hard rules — these block a PR

| # | Rule | Why |
|---|---|---|
| 1 | No `Debug.Log` — use `LocalNpcLog` | Buyers must be able to silence the package |
| 2 | No platform `#if` outside `Runtime/Platform/` | Keeps Phase 2 additive instead of a rewrite |
| 3 | No `UnityEngine.Object` access from a worker thread | Instant crash or silent corruption |
| 4 | No allocation in per-frame or per-audio-frame paths | A GC spike mid-sentence is audible |
| 5 | Every native resource is a `SafeHandle` | Domain-reload leaks crash the Editor |
| 6 | Every async method takes and honours a `CancellationToken` | Interrupt latency |
| 7 | No `async void` except Unity event handlers | Unobservable exceptions |
| 8 | No vendor type in the public `LocalNPC` namespace | Backends must stay swappable |
| 9 | **No language-specific logic outside a `LanguageProfile`-driven implementation class** | Adding a language must stay config + data |
| 10 | No `Resources.Load` — use serialized references | Asset Store guideline + build size |
| 11 | Public members have XML doc comments | The API reference is generated from them |
| 12 | No `Thread.Sleep` in a worker loop — use `BlockingCollection` | Wastes cores, adds latency |
| 13 | No `lock` held across a native call | Deadlock under cancellation |

## 10.3 Error handling
- Internal code throws; `NpcPipeline` catches at the stage boundary and converts to `NpcError`
- `OnError` fires on the main thread with a typed `NpcErrorCode`
- **Never swallow an exception silently.** Recoverable → log Warn and continue. Not recoverable → log Error and transition to `Error`
- In Editor, rethrow unexpected exceptions after logging so they appear with a stack trace

## 10.4 Performance idioms

```csharp
// yes — caller owns the buffer, no allocation
public int Resample(ReadOnlySpan<float> input, Span<float> output);

// no — allocates every call
public float[] Resample(float[] input);
```

Pool anything created more than once per second. Allocate Inference Engine tensors once and reuse; dispose deterministically. Prefer `struct` for small cross-stage data. **Profile before optimising anything not in §16.1.**

---

# PART 11 — Testing

## 11.1 Layers

| Layer | Scope | Runtime | When |
|---|---|---|---|
| Unit (EditMode) | Pure logic — splitter, prompt builder, tokenizer, resampler, ring buffer, VAD gate, mel | < 60 s | Every push |
| Integration (PlayMode) | Full pipeline with real models, faked mic | ~5 min | Every push to `dev` |
| Benchmark | §16.1 budgets, CSV artefact | ~3 min | Nightly + every stage gate |
| Soak | 30 min continuous conversation | 30 min | Every stage gate |
| Manual matrix | §11.3 | ~2 h | Before Steps 46 and 47 |

## 11.2 CI (GitHub Actions)

```yaml
on: [push, pull_request]

jobs:
  editmode:         # every push — EditMode tests, < 2 min
  build-windows:    # push to dev — Mono + IL2CPP standalone
  playmode:         # push to dev — PlayMode tests with cached models
  benchmark:        # nightly — LatencyBenchmarkTests, uploads CSV
  package-validate: # on tag — export + import into a clean project
```

Cache models by SHA in the runner cache. The ~5 MB test GGUF is committed via LFS so unit tests and `NativeSmokeTests` never need a download.

**Keep every benchmark CSV.** Latency regressions are only visible over time; a single-run assertion catches cliffs but not drift.

## 11.3 Manual test matrix

| Case | Expected |
|---|---|
| Editor Play mode | Full loop works |
| Standalone Mono | Full loop works |
| Standalone IL2CPP | Full loop works, no stripping errors |
| No microphone connected | Clear `MicrophoneUnavailable` error, no crash |
| Mic unplugged mid-utterance | Recovers to `Idle`, error event fired |
| Default audio device changed mid-session | Recaptures on the new device |
| 4 agents in one scene | Independent conversations, one shared model |
| Scene reload during generation | No leak, no orphaned thread |
| **Play-mode exit during generation** | No leaked native handle, Editor stable |
| 8 GB machine, `Fast` preset | Works |
| 8 GB machine, `Quality` selected | Clear warning, graceful failure |
| No network on first run | Clear message pointing to model download |
| Disk full mid-download | Clean failure, partial file removed |
| Corrupted model file | Hash mismatch detected, re-download offered |
| `Interrupt()` mid-sentence | Audio stops < 100 ms, state → `Idle` |
| 30-minute soak | Flat memory, no degradation |

---

# PART 12 — Debugging playbook

Ranked by how often these occur in this architecture. **Check here before asking.**

| Symptom | Likely cause | First checks |
|---|---|---|
| `DllNotFoundException` on first generate | Plugin import settings, or DLL not beside the executable | `NativeLibraryLoader` output; plugin inspector platform flags; did `BuildPostprocessor` run |
| Editor crashes on exiting Play mode | Leaked native handle across domain reload | `DomainReloadTests`; every `SafeHandle` releasing in `beforeAssemblyReload` |
| NPC answers a question nobody asked | Whisper hallucinating on silence or short clips | `SttPostProcessor` thresholds; `noSpeechProbability`; `minSpeechMs` |
| First word of every utterance missing | No pre-roll buffer | `VoiceActivityGate.preRollMs` — should be ~300 ms |
| "It cuts me off mid-sentence" | Silence hysteresis too short | `minSilenceMs` → 300–400, re-measure T1 |
| "It takes forever to respond" | Hysteresis too long, or prefix caching not working | `NpcDiagnostics` per-stage breakdown — find which stage blew its budget |
| Latency doubled after a prompt change | Prefix cache invalidated — prefix no longer byte-stable | `PromptBuilderTests` prefix-stability test; look for a timestamp or counter in the prompt |
| NPC speaks `<\|im_end\|>` or `**` aloud | `StopSequenceDetector` or `MarkupStripper` gap | Add the offending string to the test set **first**, then fix |
| Garbled non-ASCII in replies | Partial UTF-8 emitted mid-codepoint | `LlamaTokenStream` byte buffering; `TokenizerTests` emoji case |
| Audible gaps between sentences | `SpeechAudioQueue` polling `isPlaying` instead of `PlayScheduled` | Switch to `AudioSettings.dspTime` scheduling |
| Frame hitches every few seconds | GC from per-frame allocation | Profiler allocation callstacks — usually tensors in VAD or `float[]` in the resampler |
| Second agent OOMs | Model loaded per agent instead of shared | `NpcServiceLocator` ref-counting |
| Works in Editor, fails in IL2CPP | Managed stripping removed reflection-used code | Stripping level Low; add `link.xml` |
| Poor transcription accuracy | Mel mismatch, or resampler aliasing | `MelSpectrogramTests` vs golden tensors; check the 44.1 k path specifically |
| VAD triggers on the NPC's own voice | Open mic with speakers | Echo gating on `AudioSource.isPlaying` + 150 ms tail; recommend headphones or push-to-talk |
| Model download fails silently | No disk check, or partial file left with the final name | `ModelDownloader` `.part` rename pattern; disk precheck |
| A language change has no effect | Something still hardcoded | Search for literal `'.'`, `<\|en\|>`, or a concrete normalizer/phonemizer type outside the profile factories |

---

# PART 13 — Definition of Done and PR process

A task is done when **all** are true. Not "mostly."

**Code**
- [ ] Compiles with 0 warnings under Mono **and** IL2CPP
- [ ] No `Debug.Log` — uses `LocalNpcLog`
- [ ] No platform `#if` outside `Runtime/Platform/`
- [ ] No language-specific logic outside a profile-driven implementation
- [ ] No `UnityEngine.Object` access from a worker thread
- [ ] No allocation in per-frame or per-audio-frame paths (Profiler-verified once)
- [ ] Every async method takes and honours a `CancellationToken`
- [ ] Native resources wrapped in `SafeHandle`
- [ ] Public members have XML doc comments

**Tests**
- [ ] Unit tests added where the logic is testable
- [ ] Existing suite still green
- [ ] For a bug fix: a failing test was written **first**

**Review**
- [ ] Reviewed by Fahim within 48 hours of the PR opening
- [ ] Comments resolved, not just replied to

**Docs**
- [ ] §6 updated if a script was added, removed, or repurposed
- [ ] This document updated if a decision changed
- [ ] `CHANGELOG.md` entry if the behaviour is buyer-visible

**If a review is stalling you past 48 hours, say so in the daily channel.** Do not sit blocked, and do not merge unreviewed. Both are worse outcomes than a loud complaint.

---

# PART 14 — Adding a new language

The architecture supports this. The procedure is deliberately gated because two of the seven questions are research, not implementation.

## 14.1 The gate — answer all seven before writing code

1. **Does a permissively-licensed Piper voice exist for this language?** If no → separate project. Stop here.
2. **Can G2P be done for this language?** Two acceptable answers: (a) a permissive dictionary + rules implementation, or (b) an externally distributed GPL adapter via the public `IPhonemizer` (§7.4), pending the legal read. If neither → stop here.
3. Which Whisper model reaches acceptable word error rate, and what does it cost in size and latency?
4. Which LLM produces acceptable grammar in this language, at what parameter count and RAM cost?
5. Does the chat template need changing?
6. Do sentence terminators, digits, and abbreviations differ?
7. Does the subtitle font shape correctly in TextMeshPro? (Complex scripts — Bangla, Devanagari, Arabic, Thai — need explicit verification.)

**Questions 1 and 2 are research. Do not put a date on a language until both are answered yes.**

## 14.2 If the gate passes — the implementation

| Step | Work |
|---|---|
| 1 | Add an enum value to `TextNormalizerKind` and `PhonemizerKind` |
| 2 | Write `<Lang>TextNormalizer : ITextNormalizer` — calls `MarkupStripper` first, then language-specific number/currency/ordinal expansion |
| 3 | Write `<Lang>Phonemizer : IPhonemizer` — dictionary plus internal OOV fallback |
| 4 | Add the factory cases in `LanguageProfile` |
| 5 | Create `LanguageProfile_<tag>.asset` — terminators, closers, abbreviations, Whisper code, default voice |
| 6 | Add the Piper voice as a `PiperVoiceAsset` with its licence fields filled |
| 7 | Swap to a multilingual Whisper model in the catalogue |
| 8 | Swap to a capable LLM in the catalogue, or add a preset |
| 9 | Add splitter tests for the new terminators |
| 10 | Verify subtitle font shaping |

**Nothing in `NpcPipeline`, `NpcAgent`, `PiperSynthesizer`, `SentenceSplitter`, or `WhisperRecognizer` changes.** That is what the seams bought.

## 14.3 Bangla — indicative scope

Do not schedule until 14.1 questions 1 and 2 are answered. See §2.5 for the breakdown: **180–220 hours with two unresolved research risks at the front**, comparable to the entire English build through Step 30. Treat it as a distinct project, ideally triggered by a real client requirement rather than built speculatively.

---

# PART 15 — Phase 2: VR

**No Phase 2 code before v1.0 ships.** Rule §0.3 #3 exists so this phase is additive.

## 15.1 New files — 5, nothing in §6 changes

| File | Purpose |
|---|---|
| `AndroidPermissions.cs` | Runtime microphone permission, Quest-specific handling |
| `AndroidModelExtractor.cs` | `StreamingAssets` on Android lives inside the APK and is not a real file path — extract to `persistentDataPath` on first run |
| `QuestPerformanceProfile.cs` | Thread count, context, voice quality overrides per device |
| `XrPushToTalkAdapter.cs` | Controller-button gating |
| `VrSampleController.cs` | XR rig sample scene |

## 15.2 Work breakdown — ~120 h

| Task | Own | Est |
|---|---|---|
| Android arm64 packaging, import settings, `.so` verification in APK | A | 12 h |
| `AndroidPermissions` + Quest mic capture quirks | A | 10 h |
| `AndroidModelExtractor` + APK provisioning flow | A | 12 h |
| `QuestPerformanceProfile` + `PlatformCapabilities` extension | A | 8 h |
| Quest 2 profiling: hold 72 fps, 10-minute thermal soak | A | 18 h |
| `XrPushToTalkAdapter` + `VrSampleController` | F | 12 h |
| Quest 3 validation on borrowed hardware | F | 8 h |
| VR documentation | F | 10 h |
| Windows regression pass — nothing may break | A | 10 h |
| Demo capture | R | 16 h |

## 15.3 Known constraints

- **Quest 2 is the tested floor** — it's the device the team owns and the stricter one. Holding 72 Hz there means Quest 3 has headroom.
- Batch-1 decode is **memory-bandwidth bound**, not compute bound: ~400 MB of weights read per token against Quest 2's ~34 GB/s bus. Expect 15–30 tok/s while rendering. Sentence streaming stays smooth above ~12 tok/s — workable but thin.
- Quest reserves cores for the compositor. **Never request 8 threads. Use 2–3.**
- Memory: `Fast` preset only, 1024 context, **low**-quality Piper voice (25 MB, not 65 MB) → ~490 MB. The buyer's game needs the rest.
- `TtsCache` and prefix caching are **load-bearing** on Quest, not optimisations.
- **Never claim Quest 3 support without testing on a Quest 3.**

---

# PART 16 — Reference tables

## 16.1 Latency budget — `Fast` preset, i5-10400 class, target < 1000 ms

| Stage | Budget | `NpcDiagnostics` field |
|---|---|---|
| VAD end-of-speech confirmation | 200 ms | `t_vad_end` |
| Resample + preprocess | 12 ms | `t_audio_ready` |
| Whisper transcribe (3 s audio) | 180 ms | `t_transcript` |
| Prompt build + prefill (**cached prefix**) | 90 ms | `t_prefill` |
| First token | 30 ms | `t_first_token` |
| First sentence (~14 tokens @ 60 tok/s) | 230 ms | `t_first_sentence` |
| Normalize + G2P + synth | 110 ms | `t_first_pcm` |
| Clip build + playback start | 15 ms | `t_first_audio` |
| **Total** | **~867 ms** | |

Without prefix caching, prefill is ~350 ms instead of 90 ms and the budget breaks. **Step 30 is not optional.**

## 16.2 Memory by preset

| Component | `Fast` 0.5B | `Balanced` 1.5B | `Quality` 3B |
|---|---|---|---|
| LLM weights Q4_K_M | 400 MB | 1.0 GB | 2.0 GB |
| KV cache, 2048 ctx × 4 agents | 90 MB | 180 MB | 260 MB |
| Whisper tiny.en int8 | 45 MB | 45 MB | 45 MB |
| Silero VAD | 2 MB | 2 MB | 2 MB |
| Piper voice medium | 65 MB | 65 MB | 65 MB |
| Working buffers | 40 MB | 40 MB | 50 MB |
| **Total** | **~640 MB** | **~1.33 GB** | **~2.4 GB** |
| Min system RAM | 8 GB | 16 GB | 16 GB |

Update this table with Step 3's real measurements.

## 16.3 Acceptance criteria — v1.0 ships only when all are green

| # | Criterion | Target | Verified by | Step |
|---|---|---|---|---|
| T1 | End-of-speech → first audio | **< 1000 ms** | `LatencyBenchmarkTests`, median of 20 | 30 |
| T2 | Added main-thread cost | **< 1.5 ms/frame** | Profiler, 95th percentile | 29 |
| T3 | GC allocation per turn | **0 bytes** in audio + inference paths | Profiler callstacks | 29 |
| T4 | Peak RAM, `Fast` | **< 800 MB** | Profiler + working set | 36 |
| T5 | 30-minute soak | RSS flat ±5%, no crash, no T1 degradation | Soak harness | 36 |
| T6 | 4 agents, shared model | No OOM, < 3 ms/frame | `PipelineIntegrationTests` | 26 |
| T7 | Interrupt latency | **< 100 ms** | `InterruptTests` | 32 |
| T8 | Clean import | 0 errors, 0 warnings | CI | 46 |
| T9 | IL2CPP parity | All PlayMode tests pass | CI nightly | 36 |
| T10 | Domain reload | 20 cycles mid-generation, no leaks | `DomainReloadTests` | 34 |
| T11 | Cold start | Model load → `Idle` **< 8 s** | `StartupBenchmarkTests` | 36 |
| T12 | Time-to-first-NPC, new user | **< 15 min** unaided | 3 external testers, timed | 47 |

**T3 and T10 are the two that junior implementations fail silently.** Check them early and often, not at the end.

## 16.4 Package size

| Item | Cap |
|---|---|
| Asset Store package, no models | 50 MB |
| Windows native binary | 20 MB |
| Demo character + scene | 20 MB (hard cap, Rakib) |

---

# PART 17 — Documentation deliverables (Step 45)

Source in `docs/`, published as a static site.

| Doc | Length | Contents |
|---|---|---|
| Quick Start | 1 p | Import → download → drag prefab → talk. Genuinely 5 minutes |
| Installation & Requirements | 2 p | Unity version, Inference Engine version, RAM per preset, unsafe-code note |
| **Personality Writing Guide** | 3–4 p | System prompts for a small model: under 200 tokens, second person, concrete, three few-shot examples, stop strings, reply-length control. **Highest-value doc in the set** |
| Choosing a Model Preset | 2 p | Fast/Balanced/Quality with real sample outputs |
| API Reference | generated | Every public member with a sample, from XML docs |
| Performance Tuning | 3 p | Threads, context, voice quality, the §16.1 table, reading `DiagnosticsWindow` |
| Lip Sync Setup | 2 p | ARKit-52 and 15-viseme mapping, uLipSync, amplitude fallback |
| Shipping Your Game | 2 p | Model distribution modes, build size, first-run download UX |
| Offline & Kiosk Deployment | 2 p | Pre-provisioning to `StreamingAssets`, air-gapped install |
| Language Support | 1 p | English only in v1.0; the §14.1 gate explained honestly |
| Troubleshooting | 3 p | Every entry in Part 12, written for the buyer |

---

# PART 18 — Your first week

| Day | Do this |
|---|---|
| 1 | Read Parts 0–4. Ask Fahim about anything unclear **before** starting Step 1 |
| 1–2 | Step 1 — repo, project, assemblies. Verify a clean clone on a second machine |
| 2 | Step 4 — Environment Check green |
| 3–4 | Step 3 — Spike B (Whisper backend) and Spike D (throughput). **Write the numbers down.** Fahim runs Spike E (Step 3B) in parallel |
| 5 | Step 6 — logging and thread dispatch, with tests |
| 5 | Step 7 — `LanguageProfile` + the `en-US` asset |

**Do not start Stage B until the Stage A gate passes.** The spikes exist so you don't build on a wrong assumption for six weeks.

---

*Update this document at every stage gate. A specification nobody edits is a specification nobody is following.*

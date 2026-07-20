# Cross-Platform Speech-to-Text App — End-to-End Playbook for an India-Based Indie Developer (June 2026)

> **How to read this document.** Figures marked **✓** were extracted from a source and survived a 3-vote adversarial fact-check during the research run. Figures marked **(est.)** are my own synthesis/industry estimates — treat them as directional, not gospel. Competitor prices marked **(unverified)** came from blog sources whose verifier votes were cut off by a session limit; confirm them on the vendor's own pricing page before quoting them to anyone.
>
> **The honest headline first:** This is a *good* business but not an easy-money business. The OS makers give away decent dictation for free (✓ Apple Dictation and Windows Voice Access ship free with the OS). To win, you must be clearly better at one thing — **private, offline, system-wide, fast dictation that runs on the user's own hardware** — and sell it to people who feel the pain (writers, coders, doctors, lawyers, accessibility users). Done well, a solo developer can realistically build to **$2k–$10k/month (est.)** within 12–24 months. Overnight riches are not the realistic plan; a durable small software business is.

---

## Table of Contents
1. [The Opportunity & The Market](#1-the-opportunity--the-market)
2. [Competitive Landscape](#2-competitive-landscape)
3. [Which Technology: "Write Once, Run Everywhere"](#3-which-technology-write-once-run-everywhere)
4. [Which Speech Engine to Embed](#4-which-speech-engine-to-embed)
5. [The Feature Set (Detailed Spec)](#5-the-feature-set-detailed-spec)
6. [Monetization, Trials & Pricing](#6-monetization-trials--pricing)
7. [Costs: What the Developer Will Actually Spend](#7-costs-what-the-developer-will-actually-spend)
8. [Legal & Compliance for Launching from India](#8-legal--compliance-for-launching-from-india)
9. [End-to-End Roadmap (Build → Launch → Profit)](#9-end-to-end-roadmap)
10. [Sources](#10-sources)

---

## 1. The Opportunity & The Market

The wind is at your back. The broad **speech & voice recognition market is ~USD 23.70 billion in 2026, growing to USD 104.05 billion by 2034 at a 20.30% CAGR ✓** (it was USD 19.09 billion in 2025 ✓). This is a large, fast-growing category — but it includes call centers, automotive, and enterprise, so don't mistake the whole TAM for your reachable market.

**Where the money is:**
- **North America dominates — 41% of the market in 2025 (USD 1.35B), the U.S. alone USD 1.01B ✓.** This is the #1 truth for pricing: your paying customers are disproportionately American.
- **Realized lifetime value per paying user is ~$32 in North America vs ~$14 in India/SEA, $23 global median ✓.** A North American payer is worth roughly **2.3× an Indian payer.** This single fact dictates your entire pricing strategy (Section 6).
- **Highest-value buyer segments (est.):** medical dictation (doctors hate typing notes), legal, journalists/podcasters (transcription), software developers (voice coding — a fast-growing 2025–26 niche), students, and accessibility users (RSI, dyslexia, motor impairment). Accessibility and medical are the highest willingness-to-pay.

**Revenue reality by platform (est.):**
| Platform | Monetization character | Notes |
|---|---|---|
| **macOS** | **Highest revenue-per-user.** Mac users pay for utilities; one-time $30–$250 prices work. | The proven home for indie dictation apps (Superwhisper, MacWhisper, Wispr Flow all started Mac-first). **Start here.** |
| **Windows** | Largest installed base, lower willingness to pay for utilities, but huge volume. | Microsoft Store take is low; direct distribution (your own .exe + Stripe) avoids store cuts entirely. |
| **iOS** | Good ARPU, subscription-friendly, but Apple's sandbox blocks true system-wide dictation (only a keyboard extension). | Great for *transcription* and a companion to the desktop app. |
| **Android** | Highest volume, lowest ARPU, but allows deeper OS integration (real system-wide voice input). | Monetize with subscription + regional pricing; expect low per-user revenue. |

**Takeaway:** Mac + Windows desktop is where an indie makes money fastest; mobile is reach and recurring revenue. Build desktop-first.

---

## 2. Competitive Landscape

> Prices below are **(unverified)** unless marked ✓ — the fact-checker was cut off. Verify on vendor sites.

**Your free baseline competitors (the bar you must clear):**
- **Apple Dictation / Voice Control** — free with macOS & iOS ✓. On-device, decent, but not great for long-form, weak custom vocabulary, not truly cross-app-scriptable.
- **Windows Voice Access** — free with Windows 11 ✓. Improved a lot, but Windows-only and not privacy-marketed.
- **Google Live Transcribe / Gboard voice** — free on Android, cloud-leaning.

Because these exist, **"basic dictation" is worth $0.** You sell what they *don't* do well: accuracy on jargon, full offline privacy, system-wide insertion with formatting, transcription of files, and AI post-processing.

**The paid players (the proof that people pay):**
| App | Platforms | Model | Rough price (unverified) | Their wedge |
|---|---|---|---|---|
| **Superwhisper** | macOS, Windows, iOS | Offline-first, Whisper/Parakeet | ~$8.49/mo or ~$250 lifetime | Best-in-class Mac dictation UX |
| **MacWhisper** | macOS | 100% offline Whisper | ~€249 one-time | File transcription + local LLM integration |
| **Wispr Flow** | Windows, Mac, Android | Local + encrypted cloud | ~$12–15/mo | Flow/voice-writing UX, fast |
| **Otter.ai** | Web, iOS, Android | Cloud | Free 600 min → ~$10–20/mo | Meeting transcription + summaries |
| **Dragon (Nuance)** | Windows (Mac discontinued) | Local | One-time $150 → $1000+ | Medical/legal accuracy, deep vocab |
| **Rev / Fireflies / Notta / Speechify** | Cloud/mobile | Cloud | Subscription | Transcription services, accessibility reading |

> **Note on Dragon:** A claim that "Dragon costs $14.99/mo across Android/iOS/Mac/Windows at 96–99% accuracy" was **refuted 0-3** in research — Dragon's Mac product was discontinued and its model is largely one-time desktop licensing. Don't repeat that claim.

**Where the gap is (your positioning):** A **single app, truly cross-platform (Win+Mac+Linux+Android+iOS), 100% offline by default, with system-wide dictation and file transcription, sold at fair regional prices.** No single competitor cleanly owns "runs everywhere AND fully private AND affordable in India/SEA." That is your wedge.

---

## 3. Which Technology: "Write Once, Run Everywhere"

You have two layers: the **UI/app shell** (one codebase across OSes) and the **speech engine** (native, embedded — Section 4). No framework gives you 100% shared code when you need microphone capture + on-device ML + system-wide text injection; budget for ~70–85% shared and ~15–30% per-platform native glue.

| Framework | Languages | True 5-OS reach | On-device ML fit | Code reuse | Pros | Cons |
|---|---|---|---|---|---|---|
| **Flutter** | Dart | ✅ iOS, Android, Win, Mac, Linux | Good — call native via FFI/platform channels to whisper.cpp/ONNX | ★★★★☆ | One UI everywhere, fast, great mobile, strong desktop now | Desktop system-tray / global-hotkey needs plugins; heavier binaries |
| **.NET MAUI** | C# | iOS, Android, Win, Mac (Linux unofficial) | Good — C# + ONNX Runtime / whisper.net | ★★★★☆ | **You already have a C#/.NET ASR codebase** — natural fit; great Windows | Linux not first-class; smaller ecosystem than Flutter |
| **Tauri** | Rust + web (JS/TS) | ✅ all five | Excellent — Rust binds to whisper.cpp/Candle natively, tiny binaries | ★★★★☆ | Smallest installers, best for privacy/desktop, low RAM | Mobile (iOS/Android) is newer/less mature; web-UI skill needed |
| **React Native** | JS/TS | iOS, Android (+Win/Mac via community) | OK — native modules | ★★★☆☆ | Huge talent pool, great mobile | Desktop is second-class; ML bridging fiddly |
| **Kotlin Multiplatform** | Kotlin | All, but UI shared only via Compose MP | Good | ★★★☆☆ | Share logic, native UIs | More native UI work; steeper |
| **Qt** | C++/QML | ✅ all five | Excellent (C++) | ★★★★☆ | Truly native everywhere, mature | C++ complexity; commercial license cost for closed-source mobile |
| **Electron** | JS/TS | Desktop only | OK | ★★☆☆☆ | Fast to build desktop | No mobile; heavy RAM; **not recommended** as primary |

**My recommendation for your situation (est.):**
- **You already have a working .NET ASR service in this repo.** The lowest-friction path is **.NET MAUI for the app shell**, reusing your C# audio/transcription core via **ONNX Runtime / whisper.net**, and shipping **Windows + macOS + Android + iOS** from one codebase. Linux desktop you cover with a thin separate .NET console/Avalonia build.
- **If you were starting fresh and privacy/desktop-first is the brand:** **Tauri** (Rust) gives the smallest, fastest, most private desktop app and binds to whisper.cpp beautifully — add mobile later.
- **If mobile-first matters most:** **Flutter.**

**Rule of thumb:** Pick the framework whose *native ML bridging* and *your existing skills* are strongest, not the one with the prettiest demo. For you, that's the .NET/MAUI path because you already have the engine.

---

## 4. Which Speech Engine to Embed

This is where verified benchmark data helps a lot. Run **on-device** by default (privacy = your wedge; also zero per-minute cost).

**Verified 2026 benchmarks:**
- **iOS/macOS — fastest is NVIDIA Parakeet TDT v3 via CoreML: ~181.8 tok/s on iOS, ~171.6 tok/s on macOS ✓.** Use Apple Silicon's Neural Engine.
- **Android — Moonshine Tiny is fastest: 42.55 tok/s, 0.05 real-time factor ✓** (20× faster than real-time — great for live dictation).
- **Windows — Moonshine Tiny again fastest at 50.6 words/s ✓.**

> A flashy claim that "sherpa-onnx ran Whisper Tiny 51× faster than whisper.cpp on Android" was **refuted 1-2** — don't rely on that specific multiplier, though sherpa-onnx/ONNX Runtime are legitimately strong runtimes.

**Practical engine strategy:**
| Need | Engine | Why |
|---|---|---|
| **Real-time English dictation, all platforms** | **Moonshine Tiny** (via ONNX Runtime / sherpa-onnx) | Verified fastest on Android & Windows ✓; tiny footprint |
| **Apple devices, max speed** | **Parakeet TDT v3 via CoreML** | Verified fastest on iOS/macOS ✓ |
| **Best multilingual accuracy / file transcription** | **whisper.cpp** (Whisper small/medium, GGML) | 99+ languages, runs everywhere in C/C++, mature |
| **GPU desktop, batch transcription** | **faster-whisper** (CTranslate2) | Excellent throughput on NVIDIA |
| **Tiny always-listening wake/command** | **Vosk** | Lightweight, streaming |
| **Zero-binary fallback on mobile** | Apple/Android native speech APIs | Free, but cloud-ish and less private |

**Recommended default build:** Embed **whisper.cpp (small)** for accuracy + multilingual file transcription, and **Moonshine/Parakeet** for low-latency live dictation. Let the user pick a model size (speed vs accuracy). Ship models as optional downloads (your repo already does a ~670 MB model download — keep that pattern).

---

## 5. The Feature Set (Detailed Spec)

A competitive 2026 STT app is judged on these. I've grouped them as **MVP (launch)**, **V1.x (fast-follow)**, and **Differentiators (moat)**.

### MVP — must ship on day one
1. **Real-time dictation (streaming):** Speak and see text appear with <500 ms perceived latency. Partial (interim) results that finalize. This is the core loop; it must feel instant.
2. **System-wide dictation (desktop):** A global hotkey starts/stops dictation and inserts text into *whatever app has focus* (email, Word, browser, IDE) via simulated keystrokes / accessibility APIs. On iOS this is a **custom keyboard extension**; on Android, an **InputMethodEditor (IME)** or accessibility service. This "works in every app" behavior is the single most loved feature of Superwhisper/Wispr Flow.
3. **File / audio transcription:** Drag-drop an mp3/m4a/wav/video and get a timestamped transcript. Batch queue.
4. **Automatic punctuation & capitalization:** Modern models do this; expose it as on by default.
5. **Offline mode (privacy):** 100% on-device by default, clearly indicated ("🔒 Nothing leaves your device"). This is your brand.
6. **Multi-language:** At least 20+ languages via Whisper; auto-detect.
7. **Export:** Plain text, copy-to-clipboard, .txt, .srt/.vtt (subtitles), .docx, .md.
8. **Basic editing:** Edit the transcript, find/replace, playback with text highlighting (for transcription mode).

### V1.x — fast-follow (weeks after launch)
9. **Speaker diarization** ("who said what") for meetings/interviews — major value for journalists/researchers. (whisperX / pyannote-style; heavier.)
10. **Custom vocabulary / dictionary:** Names, medical/legal/technical jargon, code symbols. This is what makes doctors and lawyers pay.
11. **Voice commands / formatting:** "new line", "comma", "delete that", "all caps" — hands-free control.
12. **Per-app profiles:** Different vocabulary/format when dictating into your IDE vs your email.
13. **Cloud sync (optional, encrypted):** Sync settings/snippets across the user's own devices — explicitly opt-in, end-to-end encrypted.
14. **History & search:** Searchable archive of past dictations/transcripts (local DB).

### Differentiators — your moat
15. **AI post-processing (local LLM optional):** Auto-summarize a transcript, clean up "ums", reformat dictation into an email/bullet list, translate. Integrate with **local Ollama/LM Studio** so it stays private (MacWhisper's selling point) — and optionally a BYO-API-key cloud LLM for power users.
16. **Voice-coding mode:** Dictate code with symbol/camelCase awareness — a hot 2025–26 niche for developers.
17. **Meeting mode:** Capture system audio + mic, diarize, summarize, extract action items.
18. **Accessibility-first design:** Full keyboard control, large-text UI, screen-reader friendly — and market it to the accessibility community, which has high loyalty and willingness to pay.
19. **Truly cross-device continuity:** Start on phone, finish on laptop. Few competitors do all five OSes.

**Privacy as a feature (write this into your marketing and your privacy policy):** "Audio is processed on your device. We never upload your voice. No account required for offline use." This is both a feature and your DPDP/GDPR compliance posture (Section 8).

---

## 6. Monetization, Trials & Pricing

### Trial strategy — what the data actually says
This is the most counter-intuitive and best-supported part of the research:

- **Hard paywall (free trial that requires signup/card, then pay) converts ~10.7% vs ~2.1% for pure freemium — about 5× better ✓.** Freemium gets more signups but far fewer payers.
- **Trial length: 17–32 day trials convert at 42.5% median vs 25.5% for ≤4-day trials — long trials convert ~70% better ✓.** BUT longer trials also cancel more.
- **On mobile specifically, 5–9 day trials are the "sweet spot" (~45% median conversion, used by 52% of apps); 1–4 days is worst (~30%) ✓.**
- **Card-required ("opt-out") trials convert at 48.8% vs 18.2% for no-card ("opt-in") — 2.5–3× higher — but far fewer people start them (2.5% vs 8.5%) ✓.**

**Recommended trial design (est., grounded in the above):**
- **Desktop (your own store, Stripe):** Offer a **7-day full-feature free trial, no card up front** (lower friction, you don't have store rules forcing card), OR a generous freemium cap (e.g., 30 min of transcription/week) that nudges to a hard paywall. Lead with the 7-day trial.
- **Mobile (App Store / Play):** **7-day free trial, card required (opt-out)** via the store's native subscription. This hits the proven mobile sweet spot *and* the higher card-required conversion. Avoid 3-day — it underperforms.
- **Don't do pure unlimited freemium** — it's a 5× conversion penalty ✓. Keep the free tier *clearly limited* so the paid value is obvious.

### Subscription vs one-time
- **Mac desktop audience** historically tolerates **one-time / lifetime** pricing (Superwhisper, MacWhisper). Offer **both**: a subscription *and* a lifetime option — lifetime captures people who hate subscriptions and gives you cash up front.
- **Mobile + ongoing AI/cloud features** → **subscription** (recurring revenue, store-friendly).
- **Best of both:** subscription for the cross-device/AI tier, one-time "local-only desktop" license for privacy purists.

### Price points by country (purchasing-power-adjusted)
Anchor on the verified fact that **NA payers are worth ~$32 vs ~$14 in India/SEA ✓** — so charge the US full price and discount heavily where PPP is lower. Suggested **(est.)** tiers:

| Region | Monthly | Annual | Lifetime (desktop) | Rationale |
|---|---|---|---|---|
| **US / Canada** | $9.99 | $59–79 | $99–149 | Anchor price; highest LTV ✓ |
| **UK / EU** | £8.99 / €9.99 | £55 / €59 | €129 | Similar willingness to pay |
| **Japan** | ¥1,200 | ¥7,800 | ¥15,000 | High WTP, values quality |
| **Brazil / SEA** | ~$4–5 (local) | ~$25 | ~$49 | PPP-adjusted, price-sensitive |
| **India** | ₹199–299/mo | ₹1,499/yr | ₹2,999 | Lower LTV ✓; volume play; competes with free OS tools |

Use the stores' **price tiers + per-region overrides**, and consider PPP tooling. The goal: a US user funds the business; an Indian user is acquired cheaply and still profitable at scale.

### Store revenue cuts & rules (what each platform takes)
- **Apple App Store:** Standard 30%, **but the Small Business Program drops it to 15% for developers under $1M/yr proceeds ✓ — you will qualify, so enroll.** Subscriptions also drop to 15% after a subscriber's first year.
- **Google Play:** 15% on the first $1M/yr (and 15% on subscriptions); 30% above. India also now has more flexibility around third-party billing.
- **Microsoft Store:** Historically low/zero cut for non-game apps (Microsoft has taken 0% on apps using their own commerce at times) — and you can **sidestep stores entirely on Windows/Mac/Linux by selling direct (Stripe/Paddle/Lemon Squeezy) and keeping ~95%.** **Direct desktop sales are your highest-margin channel — prioritize them.**

> **Margin tip:** Every dollar sold through your own website via a Merchant-of-Record (Paddle/Lemon Squeezy handles global tax/VAT/GST for you, ~5%) beats a 15–30% store cut. Use stores for discovery, your site for margin.

---

## 7. Costs: What the Developer Will Actually Spend

Because you run **on-device**, your marginal cost per user is near **zero** (no per-minute STT API bills) — this is the budget developer's superpower. Cloud STT (e.g., paying per audio-minute) would destroy margins; on-device avoids it.

**One-time / annual fixed costs (est., USD):**
| Item | Cost | Notes |
|---|---|---|
| **Apple Developer Program** | **$99/yr ✓-typical** | Required for App Store + notarized Mac apps |
| **Google Play Developer** | **$25 one-time ✓-typical** | Lifetime |
| **Microsoft Store (individual)** | ~$19 one-time | Optional; or distribute direct |
| **Apple code signing** | Included in $99 | Notarization free |
| **Windows code-signing cert (OV/EV)** | ~$100–400/yr | Avoids SmartScreen warnings; **EV is pricier but removes warnings instantly** |
| **Domain + website** | ~$15–30/yr | Landing page + direct sales |
| **Email/support tool** | $0–20/mo | Start free |
| **Merchant of Record (Paddle/Lemon Squeezy)** | ~5% + $0.50/txn | Handles global tax; no upfront |
| **App store payment processing** | Built into the 15–30% cut | — |
| **Infra (only if you add cloud sync)** | $5–50/mo | A small VPS; keep optional |
| **LLM API (optional, BYO-key recommended)** | $0 to you | Let power users bring their own key |

**Bottom line:** You can **launch for under ~$250–500 in year one** (Apple $99 + Google $25 + domain + a code-signing cert). The model files are open-source (Whisper/Moonshine/Parakeet). **Your real cost is your time, not cash.** This is exactly why on-device STT is the right poverty-conscious bet — no recurring cloud bill eating your revenue.

---

## 8. Legal & Compliance for Launching from India

> Not legal/tax advice — confirm with a CA (Chartered Accountant) and a lawyer. Several specific tax claims below could not be machine-verified (the checker abstained); the structure is correct but verify current rates with a CA.

**Business structure (pick based on scale):**
- **Start as a Sole Proprietor / individual** — simplest, fine for early revenue. You can register on Play/App Store as an individual.
- **Move to an LLP or Private Limited** once revenue is meaningful (≈₹20–40 lakh/yr) — limited liability, easier to take foreign payments, more credible, and some app stores prefer a registered entity. Pvt Ltd also helps if you ever raise money.

**GST (Goods & Services Tax):**
- Software/SaaS sold from India is generally taxable, but **export of services (selling to users outside India) can be zero-rated** if you meet conditions (payment in foreign currency, LUT filed). Register for GST when you cross the threshold (₹20 lakh services, ₹10 lakh in special-category states) — or voluntarily earlier to claim input credits and to file LUT for zero-rated exports.
- Sales *within* India attract 18% GST (typically handled by the store or your Merchant of Record).

**Receiving money from abroad (FEMA / foreign remittance):**
- App-store payouts and Stripe/Paddle payouts from abroad are **inward remittances** — generally permitted under FEMA for service exports. Your bank will ask for purpose codes (e.g., software services) and may issue **FIRC/FIRA** (Foreign Inward Remittance Certificate) — keep these for GST zero-rating and audits.
- **Note:** The 20% TCS on foreign remittances that people worry about applies to *outward* remittances (money you send abroad), **not** money you receive — though one source making exactly this point could not be independently verified in the run, so confirm with your CA. Receiving export income is normal and legal.

**Income tax & digital taxes:**
- Your app income is business income — taxed at your slab (individual) or corporate rate (company). Maintain books; consider presumptive taxation (44ADA/44AD) if eligible.
- **TDS:** Stores/aggregators may deduct TDS; claim credit.
- **Equalisation levy:** India's 2% equalisation levy on foreign digital services has been a moving target in India–US trade talks (a 2026 news item on this was flagged unreliable in the run) — this mostly concerns *you paying foreign platforms*, and its status is changing. **Verify the current position with a CA before relying on it.**

**Data privacy — DPDP Act 2023 (your biggest compliance item):**
- India's **Digital Personal Data Protection Act** governs personal data of Indian users. Voice is personal data. Your **on-device, no-upload architecture is the easiest possible compliance posture** — if voice never leaves the device, you minimize obligations dramatically.
- Still required: a clear **privacy policy**, **consent** for any data you do collect (analytics, account email, optional cloud sync), purpose limitation, a way for users to delete their data, and a grievance/contact mechanism. Don't collect children's data without parental consent.
- If you sell to the EU/UK, you also touch **GDPR** — same on-device design satisfies most of it. Apple/Google **require** a privacy policy URL and a privacy "nutrition label" / Data Safety form; fill them honestly ("no data collected" if truly offline).

**Action checklist (India):**
1. Open a current account; ask the bank about foreign inward remittance (purpose codes, FIRC).
2. Get a CA; decide proprietor vs LLP/Pvt Ltd.
3. Register GST when required; file **LUT** to zero-rate exports.
4. Write a DPDP- & GDPR-compliant privacy policy (your offline design makes this easy).
5. Use a **Merchant of Record** (Paddle/Lemon Squeezy) for direct sales so global VAT/GST/tax is handled for you.
6. Keep every FIRC, invoice, and store payout statement for audits.

---

## 9. End-to-End Roadmap

**Phase 0 — Validate (2–4 weeks, ~$0):** Landing page describing the app + email waitlist. Post your idea in r/macapps, r/dictation, accessibility and dev communities. If 200+ people sign up, proceed. You already have a working .NET ASR core in this repo — that's your prototype engine.

**Phase 1 — MVP, desktop-first (6–10 weeks):** Build the Mac + Windows app (MAUI/.NET reusing your engine, or Tauri). Ship MVP features 1–8 (Section 5). Embed whisper.cpp (small) + Moonshine for live dictation. 100% offline. Sell **direct from your site** via Lemon Squeezy with a **7-day free trial**, $9.99/mo or $99 lifetime, regional pricing.

**Phase 2 — Launch & iterate (ongoing):** Ship to Microsoft Store + Mac App Store (enroll in **Apple Small Business Program → 15% ✓**). Add custom vocabulary, diarization, AI summaries (local Ollama). Gather reviews; reviews drive store ranking.

**Phase 3 — Mobile + recurring revenue (months 4–8):** Flutter/MAUI Android + iOS apps (iOS = keyboard extension; Android = IME). **7-day card-required trial** via store subscriptions. Add optional encrypted cross-device sync as the subscription hook.

**Phase 4 — Scale worldwide (months 6–24):** PPP regional pricing live in all stores. Localize the app + store listings (Japanese, German, Portuguese, Hindi). Content marketing (SEO blog: "best offline dictation app", comparison pages — note competitors rank via exactly these). Target the high-WTP niches: accessibility orgs, medical/legal, developer voice-coding.

**Realistic financial arc (est.):**
- Months 0–6: ~$0–$500/mo (launch, reviews building).
- Months 6–12: ~$500–$3,000/mo if the desktop value prop lands.
- Year 2: ~$3,000–$10,000+/mo is achievable for a well-positioned solo cross-platform privacy app — *not guaranteed*, dependent on execution, reviews, and marketing consistency.

**The three things that decide success:** (1) system-wide dictation that genuinely "just works" in every app, (2) the privacy/offline story told clearly, (3) relentless marketing (comparison content + community presence). The code is necessary but not sufficient — distribution is the hard part.

---

## 10. Sources

**Verified-claim sources (survived adversarial check):**
- RevenueCat — State of Subscription Apps (trials, conversion, regional LTV): https://www.revenuecat.com/state-of-subscription-apps/
- Adapty — Trial conversion rates: https://adapty.io/blog/trial-conversion-rates-for-in-app-subscriptions/
- VoicePing — Offline STT benchmark (Parakeet/Moonshine speeds): https://voiceping.net/en/blog/research-offline-speech-transcription-benchmark/
- Fortune Business Insights — Speech & Voice Recognition Market: https://www.fortunebusinessinsights.com/industry-reports/speech-and-voice-recognition-market-101382
- Precedence Research — AI Speech-to-Text Tool Market: https://www.precedenceresearch.com/ai-speech-to-text-tool-market
- Zapier — Best dictation software (free OS tools): https://zapier.com/blog/best-text-dictation-software/
- Qonversion — Apple 15% Small Business Program: https://qonversion.io/blog/apple-reduces-app-store-commission-to-15

**Background sources (used for context; prices treated as unverified):**
- zackproser.com voice-AI comparison 2026; freevoicereader MacWhisper vs Superwhisper; getvoibe Superwhisper platform support; codenote cross-platform tools 2026; onresonant local STT models 2026; promptquorum local Whisper comparison; mirava PPP pricing; makeanapplike India DPDP 2026; trustiics India digital tax; colorleaves app publishing cost India 2026; cleartax foreign remittance; groovyweb app store cost; getpanto mobile app stats; Fortune Business Insights medical transcription market.

*Research run: 5 angles · 25 sources fetched · 106 claims extracted · 25 adversarially verified · 13 confirmed. Final auto-synthesis was interrupted by a session limit; this document was synthesized manually from the verified claim set + domain knowledge, with verification status marked throughout.*

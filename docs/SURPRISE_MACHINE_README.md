# DBLC Surprise Machine

The **Surprise Machine** is an optional, per-campaign pacing system for
DBLC. It occasionally gives the Game Master a small packet of randomized
inspiration for an interruption, complication, opportunity, change, or
other unexpected development during normal play.

It is **not** an AI story generator, encounter generator, plot director,
or replacement for the GM. DBLC performs the timing and weighted random
selection locally; the GM decides what the result means in the fiction.
When disabled, the Surprise Machine is inert.

## Design Goals

The Surprise Machine is intended to:

-   introduce occasional unpredictability without constantly
    interrupting play;
-   give the GM inspiration without dictating an outcome;
-   require no extra LLM inference calls merely to decide whether
    something happens;
-   suppress repetitive results without banning them;
-   remain configurable without becoming a simulation system;
-   allow campaign-specific possibilities to occasionally enter the mix;
-   leave interpretation and narration to the GM.

The system generates **abstract ingredients, not finished events**.

# The Five Surprise Aspects

## 1. Magnitude

Magnitude describes roughly how consequential or disruptive the surprise
should be.

  Magnitude     Default weight
  ----------- ----------------
  `AMBIENT`                 25
  `MINOR`                   40
  `MAJOR`                   27
  `WILD`                     8

## 2. Source

Source describes **where the surprise comes from**.

There are **15 Source options**, each with a base weight of `1.0`.
Recently selected Sources are suppressed rather than prohibited; the
immediately previous Source receives a `×0.35` weighting modifier.

## 3. Change

Change describes **what kind of development occurs**.

There are **26 Change options**, each with a base weight of `1.0`.
Change participates in repetition suppression alongside Source. DBLC
preserves the last **three Source/Change pairs** for this purpose.

## 4. Behavior

Behavior describes **how an applicable actor behaves**.

There are **12 Behavior options**, each with a base weight of `1.0`.

Behavior applies only to actor-like Sources:

-   `PERSON`
-   `GROUP`
-   `AUTHORITY`
-   `CRIMINAL`
-   `ANIMAL`
-   `CREATURE`

For Sources where Behavior would not make sense, it is not forced into
the packet.

## 5. Relation

Relation describes how closely the surprise is connected to the current
situation.

  Relation                Default weight
  --------------------- ----------------
  `UNRELATED`                         70
  `INCIDENTAL`                        25
  `WORLD_CONSEQUENCE`                  5

This favors surprises that make the world feel larger than the player's
immediate actions while still allowing coincidences and occasional
consequences of existing events.

# Weighted Random Selection

The Surprise Machine uses configurable **local weighted random
selection**.

No additional LLM request is required to decide whether a Surprise
occurs, select its aspects, perform repetition suppression, or manage
pacing state. The machinery is therefore effectively **zero-LLM**.

The GM model is used where it belongs: interpreting the resulting packet
in the context of the game and narrating accordingly.

# Repetition Suppression

Pure randomness can produce valid but aesthetically annoying streaks.

The Surprise Machine remembers the last **three Source/Change pairs**
and suppresses repetition. For Source specifically, the immediately
previous Source receives a `×0.35` weighting modifier.

This is suppression, not prohibition. Repetition remains possible.

# Pacing State Machine

Each campaign maintains its own pacing state:

``` text
QUIET
  ↓
ELIGIBLE
  ↓
INTERRUPTED
  ↓
COOLDOWN
  ↓
QUIET
```

## QUIET

The Surprise Machine is waiting. Normal qualifying play advances the
pacing system toward eligibility.

## ELIGIBLE

Once eligible, eligibility is **sticky**. Each qualifying beat rolls the
configured Surprise probability until an interruption occurs. A failed
roll does not reset eligibility.

## INTERRUPTED

A successful Surprise begins an interruption. By default, the
interruption has an automatic **lease of 3--6 qualifying beats**.

The lease prevents an interruption from remaining active indefinitely
simply because nobody explicitly declares it finished.

## NORMALIZED

`NORMALIZED` is the release condition for an active interruption rather
than a separate long-lived pacing state.

When the interruption normalizes, DBLC releases it and enters cooldown.
Normalization can happen automatically after the configured interruption
duration; DBLC does not need to interpret the story to decide whether
the event is narratively over.

## COOLDOWN

After an interruption ends, the machine enters cooldown for **2--4
qualifying beats** by default. Once cooldown is complete, it returns to
`QUIET`.

# What Counts as a Beat?

The pacing system advances on **qualifying normal exchanges**, not every
technical operation DBLC performs.

Non-beats consume nothing. Internal tool activity and unrelated
application operations therefore do not accidentally accelerate Surprise
timing.

# Campaign Triggers

Campaign Triggers are player-authored, campaign-specific possibilities
that can occasionally accompany a normal Surprise.

Each Campaign Trigger contains:

-   **Name**
-   **Prompt**
-   **Weight**

A Campaign Trigger does **not replace the normal Surprise packet**. It
supplements it:

``` text
Normal Surprise packet
        +
optional Campaign Trigger
```

The default chance for a Campaign Trigger to attach to a Surprise is
**10%**, and this is configurable. Trigger weight affects selection
among the available Campaign Triggers when an attachment occurs.

# Campaign Scope

Surprise Machine state is maintained **per campaign**, including pacing
state and recent-history information used for repetition suppression.

One campaign's Surprise rhythm does not influence another campaign.
Campaign Triggers are likewise scoped to the campaign where they are
relevant.

# Settings

The Surprise Machine is an optional Settings addon.

Configurable areas include:

-   enabled/disabled state;
-   aspect weights;
-   eligibility timing;
-   Surprise probability while eligible;
-   interruption duration/lease;
-   cooldown duration;
-   Campaign Trigger attachment probability;
-   Campaign Trigger definitions and weights.

A **Reset to Defaults** action restores the shipped configuration.

# What the GM Receives

The Surprise Machine does not write the resulting scene itself. DBLC
generates the Surprise packet and makes it available to the GM, which
interprets it using the current fictional situation.

``` text
DBLC decides:
"When?"
"What abstract ingredients?"

GM decides:
"What does this mean here?"
"How does the world express it?"
```

The GM remains the Game Master.

# What the Surprise Machine Is Not

The Surprise Machine is deliberately **not**:

-   a procedural adventure generator;
-   a random encounter table;
-   a second GM;
-   an autonomous plot engine;
-   a world simulator;
-   an AI scheduler;
-   a replacement for GM judgment;
-   mandatory for every campaign.

It is a lightweight source of occasional pressure and novelty.

# Example Conceptual Flow

``` text
Campaign play continues
        ↓
QUIET pacing progresses
        ↓
Machine becomes ELIGIBLE
        ↓
Each qualifying beat rolls configured probability
        ↓
No Surprise? Remain ELIGIBLE
        ↓
Surprise succeeds
        ↓
Generate weighted Surprise aspects
        ↓
Optionally attach a Campaign Trigger
        ↓
GM receives the packet
        ↓
INTERRUPTED for configured lease
        ↓
NORMALIZED
        ↓
COOLDOWN
        ↓
QUIET
```

The GM can interpret the packet naturally rather than mechanically
announcing its fields to the player.

# Why It Exists

Long-form GM play can become overly reactive: the player acts, the GM
responds, and the world gradually begins to feel as though it only moves
when the player touches it.

The Surprise Machine provides a small counter-pressure.

Occasionally, something happens because **the world happened**.

Because its inputs are abstract rather than prewritten events, the same
machinery can work across different campaigns, genres, settings, and GM
models without DBLC needing to understand or simulate the campaign
itself.

# Design Principle

The Surprise Machine should remain:

**small, local, configurable, optional, and interpretable.**

DBLC supplies randomness and pacing.

The GM supplies meaning.

<!--
Title: the one-sentence finding, same as the main commit subject.
Keep all seven headings, in this order, even when one says "none".
See docs/fork.md for what each heading expects.
-->

## Problem

<!-- What is wrong or missing, with the evidence: numbers, file:line, task folder, issue link. -->

## Change

<!-- What was done and why this way. Alternatives rejected, one line each. -->

## Measurement

<!-- Before/after table: time, bytes per node, SHA identity of potentials, tolerances met.
     Write "Unverified" where a number is missing. Never "should be faster". -->

| | before | after |
| --- | --- | --- |
| | | |

## Tests

<!-- Each new test, and the mutation that makes it fail. The controls that keep passing. -->

## Docs

<!-- docs/*.md pages, docs/fork.md changelog entry, SPEC.md row or amendment touched. -->

## Not in this PR

<!-- Follow-ups, known gaps, deferred decisions. -->

## How to verify

<!-- The exact commands a reviewer runs: build, test filter, one einzel invocation with its expected output. -->

```
dotnet build -c Release
dotnet test -c Release --no-build --filter FullyQualifiedName~<TestClass>
```

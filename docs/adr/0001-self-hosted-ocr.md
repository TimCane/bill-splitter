# ADR-0001: Self-hosted OCR (PaddleOCR sidecar)

Status: accepted, 2026-07-04

## Context

Receipt photos must become structured line items. Options: LLM vision APIs
(best accuracy, image transits a third party), Azure Document Intelligence
(purpose-built receipt model, cloud dependency), or self-hosted OCR.
The product's core promise is that receipt data never persists and never
leaves our infrastructure.

## Decision

Self-host PaddleOCR (PP-OCRv4, English models) in a Python FastAPI sidecar.
The sidecar returns raw text lines with geometry; all parsing intelligence
lives in the C# backend (`ReceiptParser`), rules-based, fixture-tested.

## Consequences

- The privacy story is airtight: image bytes exist only in our containers.
- Accuracy will be ~70-85% of lines on real receipts - materially worse
  than LLM vision. The host review gate is therefore a first-class product
  surface, not polish, and the parser fixture corpus is a permanent
  investment.
- We run a ~2GB Python container and own its ops.
- Swapping in a better engine later only changes the sidecar; the parser
  contract (lines + boxes in, items out) is engine-agnostic.

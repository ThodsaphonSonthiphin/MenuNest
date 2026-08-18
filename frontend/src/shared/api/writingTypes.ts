export interface WritingEntryDto {
    id: string
    date: string // YYYY-MM-DD
    text: string
    elapsedSeconds: number
    wordsPerMinute: number
    correctedAt: string | null
    createdAt: string
}

export interface SubmitWritingEntryRequest {
    date: string // YYYY-MM-DD
    text: string
    elapsedSeconds: number
}

export interface UpdateWritingEntryTextRequest {
    text: string
}

export interface SentenceCombiningItem {
    source: string
    combined: string
}

export interface StuckWord {
    thai: string
    english: string
}

/**
 * One recorded Correction: the five fixed blocks plus the numbers derived
 * server-side. wordCount is the same counter behind errorsPer100Words
 * (ADR-179) -- never reconstruct it client-side from wordsPerMinute and
 * elapsedSeconds; that arithmetic was rejected as fragile (fix round 2, #97).
 */
export interface WritingCorrectionDto {
    targetRule: string
    markedText: string
    hitCount: number
    missCount: number
    thaiWhyLine: string
    sentenceCombiningItems: SentenceCombiningItem[]
    stuckWords: StuckWord[]
    errorsPer100Words: number
    wordCount: number
}

/** One entry with its Correction. `correction` is null while the night is pending. */
export interface WritingEntryDetailDto extends WritingEntryDto {
    correction: WritingCorrectionDto | null
}

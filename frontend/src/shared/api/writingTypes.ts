export interface WritingEntryDto {
    id: string
    date: string // YYYY-MM-DD
    text: string
    elapsedSeconds: number
    wordsPerMinute: number
    createdAt: string
}

export interface SubmitWritingEntryRequest {
    date: string // YYYY-MM-DD
    text: string
    elapsedSeconds: number
}

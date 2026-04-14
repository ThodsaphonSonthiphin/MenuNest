import { useCallback, useRef, useState } from 'react'
import * as SpeechSDK from 'microsoft-cognitiveservices-speech-sdk'
import { useGetSpeechTokenQuery } from '../../../shared/api/api'

export function useAzureSpeech() {
  const { data: tokenData } = useGetSpeechTokenQuery()
  const [isListening, setIsListening] = useState(false)
  const [transcript, setTranscript] = useState('')
  const [error, setError] = useState<string | null>(null)
  const recognizerRef = useRef<SpeechSDK.SpeechRecognizer | null>(null)

  const startListening = useCallback(() => {
    if (!tokenData) {
      setError('Speech token not available')
      return
    }
    setError(null)
    setTranscript('')

    const speechConfig = SpeechSDK.SpeechConfig.fromAuthorizationToken(tokenData.token, tokenData.region)
    speechConfig.speechRecognitionLanguage = 'th-TH'
    const audioConfig = SpeechSDK.AudioConfig.fromDefaultMicrophoneInput()
    const recognizer = new SpeechSDK.SpeechRecognizer(speechConfig, audioConfig)

    recognizer.recognizing = (_sender, event) => { setTranscript(event.result.text) }
    recognizer.recognized = (_sender, event) => {
      if (event.result.reason === SpeechSDK.ResultReason.RecognizedSpeech) {
        setTranscript(event.result.text)
      }
    }
    recognizer.canceled = (_sender, event) => {
      if (event.reason === SpeechSDK.CancellationReason.Error) {
        setError('ไม่สามารถรับเสียงได้ ลองใหม่อีกครั้ง')
      }
      setIsListening(false)
    }

    recognizerRef.current = recognizer
    recognizer.startContinuousRecognitionAsync(
      () => setIsListening(true),
      (err) => { setError(String(err)); setIsListening(false) },
    )
  }, [tokenData])

  const stopListening = useCallback(() => {
    recognizerRef.current?.stopContinuousRecognitionAsync(
      () => { setIsListening(false); recognizerRef.current?.close(); recognizerRef.current = null },
      () => setIsListening(false),
    )
  }, [])

  return { isListening, transcript, error, startListening, stopListening, setTranscript }
}

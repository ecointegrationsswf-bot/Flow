import { Component, type ReactNode } from 'react'

interface Props {
  children: ReactNode
}

interface State {
  hasError: boolean
  errorMessage?: string
}

export class ErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props)
    this.state = { hasError: false }
  }

  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, errorMessage: error.message }
  }

  componentDidCatch(error: Error, info: React.ErrorInfo) {
    console.error('ErrorBoundary caught:', error, info.componentStack)
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="flex min-h-screen items-center justify-center bg-gray-50">
          <div className="text-center max-w-md px-4">
            <h2 className="text-lg font-semibold text-gray-900">Algo salió mal</h2>
            <p className="mt-2 text-sm text-gray-500">Ha ocurrido un error inesperado.</p>
            {this.state.errorMessage && (
              <p className="mt-1 text-xs text-red-500 font-mono break-all">{this.state.errorMessage}</p>
            )}
            <button
              onClick={() => this.setState({ hasError: false, errorMessage: undefined })}
              className="mt-4 rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
            >
              Reintentar
            </button>
          </div>
        </div>
      )
    }

    return this.props.children
  }
}

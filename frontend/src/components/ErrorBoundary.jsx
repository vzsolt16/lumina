import { Component } from 'react'

// Catches render-time errors anywhere below it so a single bad component
// doesn't unmount the whole app and leave a blank page.
export default class ErrorBoundary extends Component {
  state = { error: null }

  static getDerivedStateFromError(error) {
    return { error }
  }

  handleReload = () => {
    window.location.reload()
  }

  render() {
    if (this.state.error) {
      return (
        <div className="panel" style={{ margin: '40px auto', maxWidth: '480px' }}>
          <div className="panel-body">
            <div className="error-line">// ERROR: Something went wrong.</div>
            <button className="btn primary" style={{ marginTop: '12px' }} onClick={this.handleReload}>
              Reload
            </button>
          </div>
        </div>
      )
    }
    return this.props.children
  }
}

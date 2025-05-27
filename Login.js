import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import axios from 'axios';
import '../styles/Login.css';
import '../styles/PawBackground.css';
import { FaPaw, FaUserShield } from 'react-icons/fa';
import PawBackground from '../components/PawBackground';

const Login = () => {
  const [showLoginForm, setShowLoginForm] = useState(false);
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [mfaStep, setMfaStep] = useState(false);
  const [mfaCode, setMfaCode] = useState('');
  const [qrCodeImage, setQrCodeImage] = useState('');
  const [message, setMessage] = useState('');
  const [isRegistering, setIsRegistering] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const navigate = useNavigate();

  const handleLoginClick = () => {
    setShowLoginForm(true);
    setIsRegistering(false);
  };

  const handleRegisterClick = () => {
    setShowLoginForm(true);
    setIsRegistering(true);
  };

  const handleSubmitRegister = async () => {
    if (username.trim() === '' || password.trim() === '') {
      setMessage('❗ Completează toate câmpurile!');
      return;
    }

    setIsLoading(true);
    setMessage('');

    try {
      const response = await axios.post('http://localhost:5056/register', {
        username: username,
        password: password,
      });

      setMessage('✅ Cont creat cu succes! Acum te poți autentifica.');
      setIsRegistering(false);
      setUsername('');
      setPassword('');
    } catch (error) {
      console.error('Registration error:', error);
      const errorMessage = error.response?.data?.title || 
                          error.response?.data || 
                          error.message || 
                          'Eroare la înregistrare';
      setMessage('❌ ' + errorMessage);
    } finally {
      setIsLoading(false);
    }
  };

  const handleSubmitLogin = async () => {
    if (username.trim() === '' || password.trim() === '') {
      setMessage('❗ Completează toate câmpurile!');
      return;
    }

    setIsLoading(true);
    setMessage('');

    try {
      const response = await axios.post('http://localhost:5056/login', {
        username: username,
        password: password,
      });

      console.log('Login response:', response.data);
      console.log('Full response object keys:', Object.keys(response.data));
      console.log('QR Code present:', !!response.data.QrCodeImageBase64);
      console.log('Requires MFA (RequiresMFA):', response.data.RequiresMFA);
      console.log('Requires MFA (requiresMFA):', response.data.requiresMFA);
      console.log('Message (Message):', response.data.Message);
      console.log('Message (message):', response.data.message);

      if (response.data.QrCodeImageBase64 || response.data.qrCodeImageBase64) {
        // Prima configurare TOTP
        console.log('Setting QR code step');
        setMfaStep(true);
        setQrCodeImage(response.data.QrCodeImageBase64 || response.data.qrCodeImageBase64);
        setMessage('📱 Scanează codul QR în Google Authenticator, apoi introdu codul generat.');
      } else if (response.data.requiresMFA || response.data.RequiresMFA) {
        // TOTP deja configurat - verificăm ambele variante
        console.log('Setting MFA step without QR');
        setMfaStep(true);
        setMessage('🔐 Introdu codul din aplicația ta de autentificare.');
      } else {
        console.log('No MFA step detected');
        console.log('Available properties:', Object.keys(response.data));
        setMessage(response.data.Message || response.data.message || 'Login processed');
      }
    } catch (error) {
      console.error('Login error:', error);
      const errorMessage = error.response?.data?.title || 
                          error.response?.data?.Message || 
                          error.response?.data || 
                          error.message || 
                          'Date de autentificare invalide';
      setMessage('❌ ' + errorMessage);
    } finally {
      setIsLoading(false);
    }
  };

  const handleSubmitMfa = async () => {
    if (mfaCode.trim() === '') {
      setMessage('❗ Introdu codul MFA!');
      return;
    }

    setIsLoading(true);

    try {
      const response = await axios.post('http://localhost:5056/verify', {
        username,
        code: mfaCode,
      });

      console.log('MFA verification response:', response.data);

      if (response.data.Token || response.data.token) {
        const token = response.data.Token || response.data.token;
        localStorage.setItem('token', token);
        localStorage.setItem('username', response.data.Username || response.data.username || username);
        setMessage('✅ Te-ai autentificat cu succes!');
        
        // Resetează starea
        setShowLoginForm(false);
        setMfaStep(false);
        setUsername('');
        setPassword('');
        setMfaCode('');
        setQrCodeImage('');
        
        // Redirect la pagina protejată sau dashboard
        setTimeout(() => {
          navigate('/animals');
        }, 1500);
      } else {
        setMessage('❌ Răspuns invalid de la server');
      }
    } catch (error) {
      console.error('MFA verification error:', error);
      const errorMessage = error.response?.data?.title || 
                          error.response?.data?.Message || 
                          error.response?.data?.message ||
                          error.response?.data || 
                          error.message || 
                          'Cod MFA invalid';
      setMessage('❌ ' + errorMessage);
    } finally {
      setIsLoading(false);
    }
  };

  const resetForm = () => {
    setShowLoginForm(false);
    setMfaStep(false);
    setIsRegistering(false);
    setUsername('');
    setPassword('');
    setMfaCode('');
    setQrCodeImage('');
    setMessage('');
  };

  return (
    <div className="login-wrapper">
      <PawBackground />
      <div className="login-container">
        <div className="header-section">
          <h1 className="title-gradient">Adopt love, adopt a life</h1>
          <p className="subtitle">Choose your entry point to continue</p>
        </div>

        <div className="auth-buttons">
          <button 
            className="auth-btn visitor"
            onClick={() => navigate('/animals')}
          >
            <FaPaw className="btn-icon" />
            <span>Continue as Guest</span>
          </button>

          <button 
            className="auth-btn staff"
            onClick={handleLoginClick}
          >
            <FaUserShield className="btn-icon" />
            <span>Staff Portal</span>
          </button>
        </div>

        {showLoginForm && (
          <div className="login-form">
            <button 
              className="close-form-btn"
              onClick={resetForm}
              style={{
                position: 'absolute',
                top: '10px',
                right: '10px',
                background: 'none',
                border: 'none',
                fontSize: '20px',
                cursor: 'pointer',
                color: '#666'
              }}
            >
              ×
            </button>

            {!mfaStep && (
              <>
                <div className="form-toggle">
                  <button
                    type="button"
                    className={`toggle-btn ${!isRegistering ? 'active' : ''}`}
                    onClick={() => setIsRegistering(false)}
                  >
                    Login
                  </button>
                  <button
                    type="button"
                    className={`toggle-btn ${isRegistering ? 'active' : ''}`}
                    onClick={() => setIsRegistering(true)}
                  >
                    Register
                  </button>
                </div>

                <form
                  onSubmit={(e) => {
                    e.preventDefault();
                    if (isRegistering) {
                      handleSubmitRegister();
                    } else {
                      handleSubmitLogin();
                    }
                  }}
                >
                  <input
                    type="text"
                    placeholder="Username"
                    className="login-input"
                    value={username}
                    onChange={(e) => setUsername(e.target.value)}
                    required
                    disabled={isLoading}
                  />
                  <input
                    type="password"
                    placeholder="Password"
                    className="login-input"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    required
                    disabled={isLoading}
                  />
                  <button 
                    type="submit" 
                    className="auth-btn staff"
                    disabled={isLoading}
                  >
                    {isLoading ? 'Loading...' : (isRegistering ? 'Create Account' : 'Submit Login')}
                  </button>
                </form>
              </>
            )}

            {mfaStep && (
              <>
                {qrCodeImage && (
                  <div className="qr-code-container">
                    <p className="subtitle">Scanează codul QR în Google Authenticator:</p>
                    <img
                      src={`data:image/png;base64,${qrCodeImage}`}
                      alt="QR Code"
                      className="qr-code"
                      style={{ width: '200px', margin: '10px auto', display: 'block' }}
                    />
                    <p style={{ fontSize: '14px', color: '#666', marginTop: '10px' }}>
                      După scanare, introdu codul de 6 cifre generat de aplicație:
                    </p>
                  </div>
                )}
                
                <form
                  onSubmit={(e) => {
                    e.preventDefault();
                    handleSubmitMfa();
                  }}
                >
                  <input
                    type="text"
                    placeholder="MFA Code (6 digits)"
                    className="login-input"
                    value={mfaCode}
                    onChange={(e) => setMfaCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
                    required
                    disabled={isLoading}
                    style={{ textAlign: 'center', fontSize: '18px', letterSpacing: '2px' }}
                  />
                  <button 
                    type="submit" 
                    className="auth-btn staff"
                    disabled={isLoading || mfaCode.length !== 6}
                  >
                    {isLoading ? 'Verifying...' : 'Submit MFA Code'}
                  </button>
                </form>
                
                <button
                  type="button"
                  onClick={() => {
                    setMfaStep(false);
                    setMfaCode('');
                    setQrCodeImage('');
                  }}
                  style={{
                    background: 'none',
                    border: '1px solid #ccc',
                    padding: '8px 16px',
                    marginTop: '10px',
                    cursor: 'pointer',
                    borderRadius: '4px'
                  }}
                >
                  ← Back to Login
                </button>
              </>
            )}
          </div>
        )}

        {message && (
          <div 
            className="message-container" 
            style={{ 
              marginTop: '15px',
              padding: '10px',
              borderRadius: '5px',
              backgroundColor: message.includes('✅') ? '#d4edda' : '#f8d7da',
              color: message.includes('✅') ? '#155724' : '#721c24',
              border: `1px solid ${message.includes('✅') ? '#c3e6cb' : '#f5c6cb'}`
            }}
          >
            {message}
          </div>
        )}

        <div className="decorative-line"></div>
        <p className="security-note">
          🔒 Your interactions are securely encrypted
        </p>
      </div>
    </div>
  );
};

export default Login;

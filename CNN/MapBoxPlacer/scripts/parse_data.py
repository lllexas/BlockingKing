"""
BoxPlacer 数据解析: (W,H,n) → 箱热力图
X: [2,20,20] (n/5 + valid_mask), y: [1,20,20] (箱二值图)
筛选: box==goal>=1, 窗口 5~20
"""
import os, sys, re, argparse
import numpy as np

MIN_SIZE, MAX_SIZE, MAX_N = 5, 20, 5
CHAR_TO_ID = {'#':1,' ':2,'@':2,'.':3,'+':3,'$':4,'*':6}
_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
LEVEL_DIR = os.path.join(_SCRIPT_DIR, "../../../Sokoban_Classic_Reference/3rdParty/Levels")
OUT_DIR = os.path.join(_SCRIPT_DIR, "../data")

def parse_levels(fp):
    with open(fp,'r',encoding='utf-8') as f: c = f.read()
    blocks = re.split(r'\n\s*\n', c.strip())
    lvls = []
    for b in blocks:
        lines = [l.rstrip() for l in b.split('\n') if not l.strip().startswith(';') and l.strip()]
        if not lines: continue
        g = []; [g.append([CHAR_TO_ID.get(c,0) for c in l]) for l in lines if l]
        if g: lvls.append(g)
    return lvls

def rectify(g):
    h=len(g); w=max(len(r) for r in g)
    r=np.full((h,w),2,dtype=np.int32)
    for i,row in enumerate(g): r[i,:len(row)]=row
    return r

def make_sample(window, n):
    h,w=window.shape
    X=np.zeros((2,h,w),dtype=np.float32)
    X[0]=n/MAX_N; X[1]=1.0
    y=np.zeros((1,h,w),dtype=np.float32)
    y[0]=((window==4)|(window==6)).astype(np.float32)
    return X,y

def augment_and_pad(X,y):
    results=[]
    for k in range(4):
        Xr=np.rot90(X,k=k,axes=(1,2)); yr=np.rot90(y,k=k,axes=(1,2))
        for fl in [False,True]:
            Xv=np.flip(Xr,axis=2) if fl else Xr
            yv=np.flip(yr,axis=2) if fl else yr
            _,h,w=Xv.shape
            Xp=np.zeros((2,MAX_SIZE,MAX_SIZE),dtype=np.float32); Xp[:,:h,:w]=Xv
            yp=np.zeros((1,MAX_SIZE,MAX_SIZE),dtype=np.float32); yp[:,:h,:w]=yv
            results.append((Xp,yp))
    return results

def main():
    p=argparse.ArgumentParser(); p.add_argument('--max-per-size',type=int,default=3); p.add_argument('--seed',type=int,default=42)
    a=p.parse_args(); np.random.seed(a.seed); os.makedirs(OUT_DIR,exist_ok=True)
    files=sorted(f for f in os.listdir(LEVEL_DIR) if f.endswith('.txt') or f.endswith('.xsb'))
    print(f"BoxPlacer 数据解析, 窗口 {MIN_SIZE}~{MAX_SIZE}")
    Xl,yl=[],[]; raw=0
    for fn in files:
        lvls=parse_levels(os.path.join(LEVEL_DIR,fn)); nb=len(Xl)
        for g in lvls:
            rect=rectify(g); h,w=rect.shape
            for wh in range(MIN_SIZE,min(MAX_SIZE+1,h+1)):
                for ww in range(MIN_SIZE,min(MAX_SIZE+1,w+1)):
                    mi,mj=h-wh,w-ww
                    if mi<0 or mj<0: continue
                    np2=min(a.max_per_size,(mi+1)*(mj+1))
                    pos=set(); att=0
                    while len(pos)<np2 and att<np2*5:
                        pos.add((np.random.randint(0,mi+1),np.random.randint(0,mj+1))); att+=1
                    for i,j in pos:
                        win=rect[i:i+wh,j:j+ww]
                        nb_=((win==4)|(win==6)).sum(); ng_=((win==3)|(win==6)).sum()
                        if nb_==ng_ and 1<=nb_<=MAX_N:
                            raw+=1; Xr,yr=make_sample(win,nb_)
                            for Xp,yp in augment_and_pad(Xr,yr): Xl.append(Xp); yl.append(yp)
        print(f"  {fn:<25s} {len(lvls):>4d}关, {len(Xl)-nb:>7d}增强 (累计{len(Xl):,})",flush=True)
    idx=np.random.permutation(len(Xl)); Xa=np.array(Xl,dtype=np.float32)[idx]; ya=np.array(yl,dtype=np.float32)[idx]
    bp=os.path.join(OUT_DIR,'data.npz'); np.savez_compressed(bp,X=Xa,y=ya)
    print(f"原始:{raw:,} 增强:{len(Xa):,} X:{Xa.shape} y:{ya.shape} → {os.path.getsize(bp)/1024/1024:.1f}MB")
    from datetime import datetime
    open(os.path.join(OUT_DIR,'diary.txt'),'w').write(f"{datetime.now():%Y-%m-%d %H:%M:%S}\nBoxPlacer\n窗口:{MIN_SIZE}~{MAX_SIZE}\n输入:[N,2,20,20](n/{MAX_N}+mask)\n标签:[N,1,20,20](箱)\n样本:{len(Xa):,}\n")

if __name__=='__main__': main()

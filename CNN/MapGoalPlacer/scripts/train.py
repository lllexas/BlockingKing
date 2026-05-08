"""
GoalPlacer 训练: 箱图 → 目标热力图
架构: Conv(2→6→10→10→6→4→1), ~2300 参数
"""
import os, sys, argparse, copy
import numpy as np
import torch, torch.nn as nn
from torch.utils.data import TensorDataset, DataLoader

BATCH=512; LR=0.001; EPOCHS=30; VAL=0.1; SEED=42; MAX_N=5; MAX_S=20
SAVE=[1,2,3,5,10,20,30]; PAT=8

_SCRIPT=os.path.dirname(os.path.abspath(__file__))
DP=os.path.join(_SCRIPT,"../data/data.npz"); WD=os.path.join(_SCRIPT,"../weights")

class Net(nn.Module):
    def __init__(self):
        super().__init__()
        self.c1=nn.Conv2d(2,6,3,padding=1); self.c2=nn.Conv2d(6,10,3,padding=1)
        self.c3=nn.Conv2d(10,10,3,padding=1); self.c4=nn.Conv2d(10,6,3,padding=1)
        self.c5=nn.Conv2d(6,4,3,padding=1); self.c6=nn.Conv2d(4,1,1)
    def forward(self,x):
        x=torch.relu(self.c1(x)); x=torch.relu(self.c2(x)); x=torch.relu(self.c3(x))
        x=torch.relu(self.c4(x)); x=torch.relu(self.c5(x)); return self.c6(x)

def mask_loss(logits,target,mask):
    bce=nn.functional.binary_cross_entropy_with_logits(logits,target,reduction='none')
    return (bce*mask).sum()/(mask.sum()+1e-8)

def top_n_recall(probs,target,mask,n_vals):
    p=probs.detach().cpu().numpy(); t=target.detach().cpu().numpy()
    m=mask.detach().cpu().numpy().astype(bool); nv=n_vals.detach().cpu().numpy().astype(int)
    recs=[]
    for i in range(len(p)):
        n=nv[i]; v=m[i,0]; pm=p[i,0].copy(); pm[~v]=-1
        top=np.argsort(pm.ravel())[-n:]; recs.append(t[i,0].ravel()[top].sum()/n)
    return np.mean(recs)

def export(model,out,ep,m):
    os.makedirs(out,exist_ok=True); s=model.state_dict()
    for i in range(1,6):
        s[f"c{i}.weight"].cpu().numpy().transpose(2,3,1,0).tofile(f"{out}/conv{i}_weight.bin")
        s[f"c{i}.bias"].cpu().numpy().tofile(f"{out}/conv{i}_bias.bin")
    s["c6.weight"].cpu().numpy().transpose(2,3,1,0).tofile(f"{out}/conv6_weight.bin")
    s["c6.bias"].cpu().numpy().tofile(f"{out}/conv6_bias.bin")
    from datetime import datetime
    np_=sum(p.numel() for p in model.parameters())
    with open(f"{out}/diary.txt",'w') as f:
        f.write(f"{datetime.now():%Y-%m-%d %H:%M:%S}\nEpoch:{ep}\n参数:{np_}\n")
        for k,v in m.items(): f.write(f"{k}:{v:.6f}\n")

def train(args):
    torch.manual_seed(SEED); np.random.seed(SEED)
    dev=torch.device('cuda' if torch.cuda.is_available() else 'cpu')
    print(f"设备:{dev}")
    d=np.load(DP); X,y=d['X'],d['y']
    # 从箱图推断 n: 箱 count
    n_vals = (X[:,0].sum(axis=(1,2)) + 0.5).astype(int)
    nv=int(len(X)*VAL); idx=np.random.permutation(len(X))
    Xt,Xv_=X[idx[:-nv]],X[idx[-nv:]]; yt,yv_=y[idx[:-nv]],y[idx[-nv:]]
    nt,nv_vals=n_vals[idx[:-nv]],n_vals[idx[-nv:]]
    print(f"训练:{len(Xt):,} 验证:{len(Xv_):,}")
    Xtt=torch.from_numpy(Xt).float(); ytt=torch.from_numpy(yt).float(); ntt=torch.from_numpy(nt).long()
    Xvt=torch.from_numpy(Xv_).float(); yvt=torch.from_numpy(yv_).float(); nvt=torch.from_numpy(nv_vals).long()
    tr=DataLoader(TensorDataset(Xtt,ytt,ntt),BATCH,shuffle=True,pin_memory=True)
    va=DataLoader(TensorDataset(Xvt,yvt,nvt),BATCH*2,pin_memory=True)
    model=Net().to(dev); print(f"参数:{sum(p.numel() for p in model.parameters())}")
    opt=torch.optim.Adam(model.parameters(),lr=LR)
    print(f"\n训练{EPOCHS}轮 patience={PAT}"); print("-"*65)
    print(f"{'Ep':>4s} {'TrLoss':>8s} {'VlLoss':>8s} {'R@N':>6s} {'Stale':>5s}"); print("-"*65)
    best=float('inf'); bs=None; st=0; stp=False; sp=1
    if args.resume:
        ck=torch.load(args.resume,map_location=dev,weights_only=False)
        model.load_state_dict(ck['model']); opt.load_state_dict(ck['optimizer']); sp=ck['epoch']+1
        print(f"续跑 ep{sp}")
    for ep in range(sp,EPOCHS+1):
        model.train(); tl=0.0
        for bx,by,bn in tr:
            bx,by=bx.to(dev,non_blocking=True),by.to(dev,non_blocking=True); mk=bx[:,1:2]
            opt.zero_grad(); lo=mask_loss(model(bx),by,mk); lo.backward(); opt.step()
            tl+=lo.item()*bx.size(0)
        tl/=len(Xt)
        model.eval(); vl=0.0; ap,at,am,an=[],[],[],[]
        with torch.no_grad():
            for bx,by,bn in va:
                bx,by=bx.to(dev),by.to(dev); mk=bx[:,1:2]; bn=bn.to(dev)
                lo=mask_loss(model(bx),by,mk); vl+=lo.item()*bx.size(0)
                ap.append(torch.sigmoid(model(bx))); at.append(by); am.append(mk); an.append(bn)
        vl/=len(Xv_); rn=top_n_recall(torch.cat(ap),torch.cat(at),torch.cat(am),torch.cat(an))
        imp=vl<best-1e-6
        if imp: best=vl; bs=copy.deepcopy(model.state_dict()); st=0
        else: st+=1
        print(f"{ep:>4d} {tl:>8.4f} {vl:>8.4f} {rn:>5.1%} {st:>4d}{' *' if imp else ''}",flush=True)
        if ep in SAVE:
            export(model,f"{WD}/epoch{ep:02d}",ep,{'val_loss':vl,'r@n':rn})
            torch.save({'epoch':ep,'model':model.state_dict(),'optimizer':opt.state_dict(),'val_loss':vl},f"{WD}/latest.pt")
            print(f"  → 存 ep{ep}",flush=True)
        if st>=PAT: print(f"\n  Early stop @{ep} best={best:.6f}",flush=True); stp=True; break
    if stp and bs: model.load_state_dict(bs)
    print("-"*65); print(f"Best:{best:.6f} R@N:{rn:.1%}")

if __name__=='__main__':
    p=argparse.ArgumentParser(); p.add_argument('--epochs',type=int,default=EPOCHS); p.add_argument('--resume',type=str,default='')
    a=p.parse_args(); EPOCHS=a.epochs; SAVE=sorted(set([1]+[e for e in [2,3,5,10,20,30] if e<=EPOCHS]))
    train(a)
